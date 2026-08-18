<script lang="ts">
	// The trace-detail equivalent of resources/ResourceGraph.svelte - same
	// SvelteFlow + dagre (`layoutGraph`, reused as-is) pairing, same dark-colorMode
	// requirement (SvelteFlow ships light-mode CSS by default; see that component's
	// remarks for the broken-white-on-white-boxes failure mode this avoids).
	import { SvelteFlow, Background, Controls, type Edge } from '@xyflow/svelte';
	import '@xyflow/svelte/dist/style.css';
	import type { SpanDto } from '$lib/traces-api';
	import { buildServiceMap } from '$lib/traces/service-map';
	import type { ServiceMapFlowNode } from '$lib/traces/service-map-flow-types';
	import { layoutGraph } from '$lib/resources/layout';
	import ServiceMapNode from './ServiceMapNode.svelte';
	import * as Empty from '$lib/components/ui/empty';

	let { spans }: { spans: SpanDto[] } = $props();

	const nodeTypes = { 'service-map': ServiceMapNode };

	let nodes = $state.raw<ServiceMapFlowNode[]>([]);
	let edges = $state.raw<Edge[]>([]);

	$effect(() => {
		const built = buildServiceMap(spans);

		const rawNodes: ServiceMapFlowNode[] = built.nodes.map((service) => ({
			id: service.service,
			type: 'service-map',
			data: { service },
			position: { x: 0, y: 0 }
		}));
		const rawEdges: Edge[] = built.edges.map((edge) => ({
			id: `${edge.source}->${edge.target}`,
			source: edge.source,
			target: edge.target,
			animated: true
		}));

		nodes = layoutGraph(rawNodes, rawEdges);
		edges = rawEdges;
	});
</script>

{#if nodes.length <= 1}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Title>Nothing to map</Empty.Title>
			<Empty.Description>
				Every span in this trace belongs to the same service - the map has nothing to draw beyond it.
			</Empty.Description>
		</Empty.Header>
	</Empty.Root>
{:else}
	<div class="h-full min-h-0 w-full">
		<SvelteFlow bind:nodes bind:edges {nodeTypes} colorMode="dark" fitView>
			<Background />
			<Controls />
		</SvelteFlow>
	</div>
{/if}
