<script lang="ts">
	// "Ingestion topology" - a compact flow diagram of the same pipeline the tiles/tables
	// elsewhere on this page already describe piecemeal (receivers -> per-signal stream
	// buffer -> flush worker -> ClickHouse, plus a rejected-payloads side path), reusing
	// this page's own health vocabulary (ingestion/health.ts) to color each node instead of
	// introducing a second opinion about what "unhealthy" means. Not new data - a synthesis
	// of ingestion.stats/ingestion.pipeline into one picture of where telemetry is flowing
	// and where it's getting stuck, the thing a stack of separate tables can't show at a
	// glance.
	//
	// A toolbar button opening a Dialog, not a collapsible section anchored at the bottom
	// of the page (this component's first shape) - that placement made it easy to miss on
	// an already-long page (feedback: couldn't find it after scrolling past three other
	// table sections), and self-contained button+dialog is this app's own established shape
	// for an on-demand secondary view (ExportDialog on the Logs page is the precedent: the
	// trigger button and the dialog content live in one component, dropped into a toolbar).
	//
	// Reuses @xyflow/svelte + @dagrejs/dagre + resources/layout.ts's layoutGraph wholesale -
	// already proven on the Resources page's own graph (ResourceGraph.svelte), and
	// layoutGraph only ever needed `.id`/`.source`/`.target`, so it's exactly as reusable
	// for this unrelated node/edge set as it was there.
	import { SvelteFlow, Background, Controls, type Edge } from '@xyflow/svelte';
	import '@xyflow/svelte/dist/style.css';
	import * as Dialog from '$lib/components/ui/dialog';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import WaypointsIcon from '@lucide/svelte/icons/waypoints';
	import { ingestionContext } from '$lib/ingestion/context';
	import { layoutGraph } from '$lib/resources/layout';
	import TopologyNode from './TopologyNode.svelte';
	import { formatAge, formatCount, secondsSince, signalLabel } from '$lib/ingestion/format';
	import * as m from '$lib/paraglide/messages';
	import {
		DOWN_UTILIZATION_PERCENT,
		WARN_UTILIZATION_PERCENT,
		computeFlushStatus,
		hasRecentArrivals,
		isBacklogStuck,
		utilizationPercent
	} from '$lib/ingestion/health';
	import type { IngestionTopologyNode, TopologyLine, TopologyTone } from '$lib/ingestion/topology-types';
	import type { IngestionSignal } from '$lib/ingestion-api';

	let { open = $bindable(false) }: { open?: boolean } = $props();

	const ingestion = ingestionContext.get();

	const SIGNALS: IngestionSignal[] = ['Logs', 'Traces', 'Metrics'];
	const nodeTypes = { 'ingestion-topology': TopologyNode };

	let nodes = $state.raw<IngestionTopologyNode[]>([]);
	let edges = $state.raw<Edge[]>([]);

	$effect(() => {
		// Only build the graph while the dialog is actually open - no point doing this work
		// (or holding onto stale positions) while it's closed.
		if (!open) return;

		const stats = ingestion.stats;
		const pipeline = ingestion.pipeline;
		if (!stats || !pipeline) return;

		const streamBySignal = new Map(pipeline.streams.map((s) => [s.signal, s]));
		const workerBySignal = new Map(pipeline.flushWorkers.map((w) => [w.signal, w]));

		const topoNodes: IngestionTopologyNode[] = [
			{
				id: 'receivers',
				type: 'ingestion-topology',
				position: { x: 0, y: 0 },
				data: {
					kind: 'receivers',
					title: m.ingestionTopology_receiversTitle(),
					tone: 'default',
					lines: [
						{
							label: m.ingestionTopology_ingressLabel(),
							value: m.ingestionTopology_ingressValue({ count: formatCount(stats.totals.arrivalsPerMinute) })
						}
					],
					// Same "gRPC :4317"/"HTTP :4318" wording as IngestionReceivers.svelte's own
					// receiver labels - reuses its message keys rather than defining a third
					// identical pair here (see ingestion/format.ts's own remarks on the
					// deliberately-duplicated-per-surface *arrays*; the translated text itself
					// doesn't need a fourth copy).
					badges: [m.ingestionReceivers_grpcLabel(), m.ingestionReceivers_httpLabel()]
				}
			},
			{
				id: 'storage',
				type: 'ingestion-topology',
				position: { x: 0, y: 0 },
				// "ClickHouse" - a product name, not translated (same convention as the
				// <code>system.part_log</code>-style identifiers elsewhere on this page).
				data: { kind: 'storage', title: 'ClickHouse', tone: 'default', lines: [] }
			}
		];

		const topoEdges: Edge[] = [];

		for (const signal of SIGNALS) {
			const stream = streamBySignal.get(signal);
			const worker = workerBySignal.get(signal);

			const pct = stream ? utilizationPercent(stream) : null;
			const streamStuck = stream ? isBacklogStuck(stream) : false;
			const streamTone: TopologyTone =
				(pct !== null && pct >= DOWN_UTILIZATION_PERCENT) || streamStuck
					? 'destructive'
					: pct !== null && pct >= WARN_UTILIZATION_PERCENT
						? 'warning'
						: 'default';

			const streamLines: TopologyLine[] = stream
				? [
						{
							label: m.ingestionTopology_bufferedLabel(),
							value:
								pct !== null
									? m.ingestionTopology_bufferedValuePercent({ count: formatCount(stream.length), percent: pct })
									: formatCount(stream.length),
							tone: streamTone
						},
						...(streamStuck
							? ([
									{
										label: m.ingestionTopology_pendingLabel(),
										value: m.ingestionTopology_pendingStuckValue({ count: formatCount(stream.pendingCount) }),
										tone: 'destructive'
									}
								] as TopologyLine[])
							: [])
					]
				: [{ label: m.ingestionTopology_bufferedLabel(), value: m.ingestionTopology_noTrafficYet() }];

			topoNodes.push({
				id: `stream-${signal}`,
				type: 'ingestion-topology',
				position: { x: 0, y: 0 },
				data: { kind: 'stream', title: signalLabel(signal), tone: streamTone, lines: streamLines }
			});

			// Same computeFlushStatus PipelineFlushHealthTable uses - a worker that recovered
			// (consecutiveErrors back to 0 after a real lastError) shouldn't render this node
			// destructive/warning just because *a* lastError string exists; 'good'/'default'
			// both read as a plain, uncolored border here (this diagram has no green-highlight
			// treatment, unlike the table's own check icon).
			const flushStatus = worker ? computeFlushStatus(worker, stream, hasRecentArrivals(stats.buckets, signal)) : null;
			const workerTone: TopologyTone =
				flushStatus?.tone === 'destructive' ? 'destructive' : flushStatus?.tone === 'warning' ? 'warning' : 'default';

			const workerLines: TopologyLine[] = worker
				? [
						{
							label: m.ingestionTopology_lastFlushLabel(),
							value: worker.lastFlushAt ? formatAge(secondsSince(worker.lastFlushAt)) : m.ingestionTopology_neverValue()
						},
						{ label: m.ingestionTopology_statusLabel(), value: flushStatus!.label, tone: workerTone }
					]
				: [];

			topoNodes.push({
				id: `worker-${signal}`,
				type: 'ingestion-topology',
				position: { x: 0, y: 0 },
				data: { kind: 'worker', title: m.ingestionTopology_consumerTitle({ signal: signalLabel(signal) }), tone: workerTone, lines: workerLines }
			});

			topoEdges.push(
				{ id: `receivers->stream-${signal}`, source: 'receivers', target: `stream-${signal}`, animated: true },
				{ id: `stream-${signal}->worker-${signal}`, source: `stream-${signal}`, target: `worker-${signal}`, animated: true },
				{ id: `worker-${signal}->storage`, source: `worker-${signal}`, target: 'storage', animated: true }
			);
		}

		// Only appears when something's actually being rejected right now - a receiver-level
		// refusal never entered the buffered pipeline at all, so it's drawn as a side path off
		// Receivers, not a fifth stage every signal always has.
		if (stats.totals.rejectedInWindow > 0) {
			topoNodes.push({
				id: 'rejected',
				type: 'ingestion-topology',
				position: { x: 0, y: 0 },
				data: {
					kind: 'rejected',
					title: m.ingestionTopology_rejectedTitle(),
					tone: 'destructive',
					lines: [
						{ label: m.ingestionTopology_thisWindowLabel(), value: formatCount(stats.totals.rejectedInWindow), tone: 'destructive' }
					]
				}
			});
			topoEdges.push({
				id: 'receivers->rejected',
				source: 'receivers',
				target: 'rejected',
				style: 'stroke: var(--destructive); stroke-dasharray: 4 3;'
			});
		}

		nodes = layoutGraph<IngestionTopologyNode>(topoNodes, topoEdges);
		edges = topoEdges;
	});
</script>

<Dialog.Root {open} onOpenChange={(v) => (open = v)}>
	<Dialog.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="outline" size="sm">
				<WaypointsIcon data-icon="inline-start" />
				{m.ingestionTopology_triggerLabel()}
			</Button>
		{/snippet}
	</Dialog.Trigger>
	<Dialog.Content class="flex h-[80vh] w-[80vw] max-w-[80vw] flex-col sm:max-w-[80vw]">
		<Dialog.Header class="shrink-0">
			<Dialog.Title>{m.ingestionTopology_dialogTitle()}</Dialog.Title>
			<Dialog.Description>{m.ingestionTopology_dialogDescription()}</Dialog.Description>
		</Dialog.Header>
		{#if !ingestion.stats || !ingestion.pipeline}
			<div class="flex flex-1 items-center justify-center">
				<Spinner />
			</div>
		{:else}
			<!-- colorMode="dark" - see ResourceGraph.svelte's own remarks; this dashboard is
			     dark-only and SvelteFlow's default light-mode CSS renders Controls/edge labels
			     as broken white-on-white boxes without it. -->
			<div class="min-h-0 w-full flex-1 overflow-hidden rounded-md border">
				<SvelteFlow bind:nodes bind:edges {nodeTypes} colorMode="dark" fitView minZoom={0.4} nodesDraggable={false}>
					<Background />
					<Controls />
				</SvelteFlow>
			</div>
		{/if}
	</Dialog.Content>
</Dialog.Root>