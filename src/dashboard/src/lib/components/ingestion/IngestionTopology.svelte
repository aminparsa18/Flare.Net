<script lang="ts">
	// "Ingestion topology" - a compact, collapsible flow diagram of the same pipeline the
	// tiles/tables above already describe piecemeal (receivers -> per-signal stream buffer
	// -> flush worker -> ClickHouse, plus a rejected-payloads side path), reusing this
	// page's own health vocabulary (ingestion/health.ts) to color each node instead of
	// introducing a second opinion about what "unhealthy" means. Not new data - a synthesis
	// of ingestion.stats/ingestion.pipeline into one picture of where telemetry is flowing
	// and where it's getting stuck, the thing a stack of separate tables can't show at a
	// glance.
	//
	// Reuses @xyflow/svelte + @dagrejs/dagre + resources/layout.ts's layoutGraph wholesale -
	// already proven on the Resources page's own graph (ResourceGraph.svelte), and
	// layoutGraph only ever needed `.id`/`.source`/`.target`, so it's exactly as reusable
	// for this unrelated node/edge set as it was there.
	import { browser } from '$app/environment';
	import { SvelteFlow, Background, Controls, type Edge } from '@xyflow/svelte';
	import '@xyflow/svelte/dist/style.css';
	import * as Accordion from '$lib/components/ui/accordion';
	import { Spinner } from '$lib/components/ui/spinner';
	import { ingestionContext } from '$lib/ingestion/context';
	import { layoutGraph } from '$lib/resources/layout';
	import TopologyNode from './TopologyNode.svelte';
	import { formatAge, formatCount, secondsSince } from '$lib/ingestion/format';
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

	const ingestion = ingestionContext.get();

	// Same single-item-Accordion collapse/expand + localStorage-persisted preference
	// VolumeChart.svelte established - open by default (this is meant to be found, not
	// hidden away), remembering a reader's choice to collapse it across visits.
	const TOPOLOGY_ITEM = 'topology';
	const COLLAPSE_STORAGE_KEY = 'flare.ingestion.topologyCollapsed';

	function loadStoredValue(): string {
		if (!browser) return TOPOLOGY_ITEM;
		try {
			return localStorage.getItem(COLLAPSE_STORAGE_KEY) === 'true' ? '' : TOPOLOGY_ITEM;
		} catch {
			return TOPOLOGY_ITEM; // storage disabled (e.g. private browsing) - fall back to expanded
		}
	}

	let accordionValue = $state(loadStoredValue());

	$effect(() => {
		const value = accordionValue;
		if (!browser) return;
		try {
			localStorage.setItem(COLLAPSE_STORAGE_KEY, String(value !== TOPOLOGY_ITEM));
		} catch {
			// Non-critical - the next reload just falls back to expanded instead.
		}
	});

	const collapsed = $derived(accordionValue !== TOPOLOGY_ITEM);

	const SIGNALS: IngestionSignal[] = ['Logs', 'Traces', 'Metrics'];
	const nodeTypes = { 'ingestion-topology': TopologyNode };

	let nodes = $state.raw<IngestionTopologyNode[]>([]);
	let edges = $state.raw<Edge[]>([]);

	$effect(() => {
		// Skip rebuilding a hidden graph on every poll tick - same "don't do work behind a
		// collapsed section" reasoning VolumeChart's own fetch effect documents.
		if (collapsed) return;

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
					title: 'OTLP Receivers',
					tone: 'default',
					lines: [{ label: 'Ingress', value: `${formatCount(stats.totals.arrivalsPerMinute)} req/min` }],
					badges: ['gRPC :4317', 'HTTP :4318']
				}
			},
			{
				id: 'storage',
				type: 'ingestion-topology',
				position: { x: 0, y: 0 },
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
							label: 'Buffered',
							value: pct !== null ? `${formatCount(stream.length)} (${pct}%)` : formatCount(stream.length),
							tone: streamTone
						},
						...(streamStuck
							? ([{ label: 'Pending', value: `${formatCount(stream.pendingCount)} stuck`, tone: 'destructive' }] as TopologyLine[])
							: [])
					]
				: [{ label: 'Buffered', value: 'no traffic yet' }];

			topoNodes.push({
				id: `stream-${signal}`,
				type: 'ingestion-topology',
				position: { x: 0, y: 0 },
				data: { kind: 'stream', title: signal, tone: streamTone, lines: streamLines }
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
						{ label: 'Last flush', value: worker.lastFlushAt ? formatAge(secondsSince(worker.lastFlushAt)) : 'never' },
						{ label: 'Status', value: flushStatus!.label, tone: workerTone }
					]
				: [];

			topoNodes.push({
				id: `worker-${signal}`,
				type: 'ingestion-topology',
				position: { x: 0, y: 0 },
				data: { kind: 'worker', title: `${signal} consumer`, tone: workerTone, lines: workerLines }
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
					title: 'Rejected',
					tone: 'destructive',
					lines: [{ label: 'This window', value: formatCount(stats.totals.rejectedInWindow), tone: 'destructive' }]
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

<Accordion.Root type="single" bind:value={accordionValue} class="w-full flex-col rounded-none border-0 border-t">
	<Accordion.Item value={TOPOLOGY_ITEM} class="border-0 data-open:bg-transparent">
		<div class="flex items-center justify-between gap-2 px-4 py-3 text-xs">
			<Accordion.Trigger
				class="text-muted-foreground hover:text-foreground group/accordion-trigger relative flex w-auto flex-none items-center justify-start gap-1 border-none p-0 text-left text-xs font-normal hover:no-underline **:data-[slot=accordion-trigger-icon]:ml-0 **:data-[slot=accordion-trigger-icon]:size-3.5"
			>
				Ingestion topology
			</Accordion.Trigger>
		</div>
		<Accordion.Content class="px-4 pb-4">
			{#if !ingestion.stats || !ingestion.pipeline}
				<div class="flex h-[200px] items-center justify-center">
					<Spinner />
				</div>
			{:else}
				<!-- colorMode="dark" - see ResourceGraph.svelte's own remarks; this dashboard is
				     dark-only and SvelteFlow's default light-mode CSS renders Controls/edge
				     labels as broken white-on-white boxes without it. -->
				<div class="h-[280px] w-full overflow-hidden rounded-md border">
					<SvelteFlow bind:nodes bind:edges {nodeTypes} colorMode="dark" fitView minZoom={0.4} nodesDraggable={false}>
						<Background />
						<Controls />
					</SvelteFlow>
				</div>
			{/if}
		</Accordion.Content>
	</Accordion.Item>
</Accordion.Root>