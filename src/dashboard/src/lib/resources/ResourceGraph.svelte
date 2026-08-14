<script lang="ts">
	import { SvelteFlow, Background, Controls, type Edge } from '@xyflow/svelte';
	import '@xyflow/svelte/dist/style.css';
	import type { ResourceGraphSnapshot } from '$lib/api';
	import type { FlareResourceNode } from './types';
	import { layoutGraph } from './layout';
	import ResourceNode from './ResourceNode.svelte';

	let { snapshot }: { snapshot: ResourceGraphSnapshot } = $props();

	const nodeTypes = { 'flare-resource': ResourceNode };

	// node.id is the role (e.g. "clickhouse"), not the underlying container ID - stable
	// across polls (and matches ResourceEdgeDto.SourceRole/TargetRole), unlike the
	// container ID, which changes across a recreate. Recomputed wholesale on every
	// snapshot - see layoutGraph's remarks for why that's fine here.
	let nodes = $state.raw<FlareResourceNode[]>([]);
	let edges = $state.raw<Edge[]>([]);

	$effect(() => {
		const rawNodes: FlareResourceNode[] = snapshot.nodes.map((node) => ({
			id: node.role,
			type: 'flare-resource',
			data: { node },
			position: { x: 0, y: 0 }
		}));
		const rawEdges: Edge[] = snapshot.edges.map((edge) => ({
			id: `${edge.sourceRole}->${edge.targetRole}`,
			source: edge.sourceRole,
			target: edge.targetRole,
			label: edge.relationshipType,
			animated: true
		}));

		nodes = layoutGraph(rawNodes, rawEdges);
		edges = rawEdges;
	});
</script>

<div class="h-full min-h-0 w-full">
	<!-- colorMode="dark" from the start - this dashboard is dark-only (see app.html) and
	     SvelteFlow ships light-mode CSS by default; skipping this renders Controls and edge
	     labels as broken white-on-white boxes (discovered the hard way in a same-day
	     exploration of this exact feature - see docs/prompts/docker-resources-graph-prompt.md). -->
	<SvelteFlow bind:nodes bind:edges {nodeTypes} colorMode="dark" fitView>
		<Background />
		<Controls />
	</SvelteFlow>
</div>
