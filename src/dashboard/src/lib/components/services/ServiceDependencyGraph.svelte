<script lang="ts">
	// The Services tab's "Map" view - the aggregate, cross-trace counterpart to the
	// trace-detail page's own ServiceMap.svelte. Same SvelteFlow + dagre (`layoutGraph`)
	// pairing and the very same `ServiceMapNode.svelte` card, since `services-api.ts`
	// already reshapes the wire DTOs into `service-map.ts`'s own `ServiceMapNode`/
	// `ServiceMapEdge` types (see its own remarks) - this component only differs from
	// ServiceMap.svelte in where its nodes/edges come from (already-aggregated data off
	// `ServicesState.graph`, not a client-side walk over one trace's spans).
	import { SvelteFlow, Background, Controls, type Edge } from '@xyflow/svelte';
	import '@xyflow/svelte/dist/style.css';
	import type { ServiceDependencyGraph } from '$lib/services-api';
	import type { ServiceMapFlowNode } from '$lib/traces/service-map-flow-types';
	import { layoutGraph } from '$lib/resources/layout';
	import { formatDurationNano } from '$lib/traces/duration';
	import ServiceMapNode from '$lib/components/traces/ServiceMapNode.svelte';
	import * as Empty from '$lib/components/ui/empty';
	import { servicesContext } from '$lib/services/context';
	import * as m from '$lib/paraglide/messages';

	let { graph }: { graph: ServiceDependencyGraph | null } = $props();

	const services = servicesContext.get();

	const nodeTypes = { 'service-map': ServiceMapNode };

	let nodes = $state.raw<ServiceMapFlowNode[]>([]);
	let edges = $state.raw<Edge[]>([]);

	$effect(() => {
		if (!graph) {
			nodes = [];
			edges = [];
			return;
		}

		const rawNodes: ServiceMapFlowNode[] = graph.nodes.map((service) => ({
			id: service.service,
			type: 'service-map',
			data: { service },
			position: { x: 0, y: 0 }
		}));
		// Same "1 call · 61ms" / "N calls · avg Xms" edge label as ServiceMap.svelte -
		// avg latency is still the right headline number once an edge sums many calls
		// across many traces, not just the handful within one.
		const rawEdges: Edge[] = graph.edges.map((edge) => ({
			id: `${edge.source}->${edge.target}`,
			source: edge.source,
			target: edge.target,
			animated: true,
			label:
				edge.callCount === 1
					? m.serviceMap_edgeSingleCall({ duration: formatDurationNano(edge.totalDurationNano) })
					: m.serviceMap_edgeMultiCall({
							count: edge.callCount,
							duration: formatDurationNano(edge.totalDurationNano / edge.callCount)
						})
		}));

		nodes = layoutGraph(rawNodes, rawEdges);
		edges = rawEdges;
	});
</script>

{#if nodes.length <= 1}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Title>{m.serviceDependencyGraph_emptyTitle()}</Empty.Title>
			<Empty.Description>
				{m.serviceDependencyGraph_emptyDescription()}
			</Empty.Description>
		</Empty.Header>
	</Empty.Root>
{:else}
	<div class="h-full min-h-0 w-full">
		<!-- onnodeclick opens the per-node drill-down (ServiceCallBreakdownDialog, rendered
		     as this tab's own sibling - see ServicesState.selectedService's remarks) rather
		     than anything inline here - a node card has no room for a second data fetch's
		     worth of detail, and this is the one place in the app the aggregate Map view
		     differs behaviorally from the per-trace ServiceMap.svelte it otherwise reuses
		     verbatim (that one has nothing further to drill into - a trace's own waterfall
		     already is the detail view). -->
		<SvelteFlow
			bind:nodes
			bind:edges
			{nodeTypes}
			colorMode="dark"
			fitView
			onnodeclick={({ node }) => (services.selectedService = node.id)}
		>
			<Background />
			<Controls />
		</SvelteFlow>
	</div>
{/if}
