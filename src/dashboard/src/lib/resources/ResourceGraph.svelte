<script lang="ts">
	import { SvelteFlow, Background, Controls, type Edge } from '@xyflow/svelte';
	import '@xyflow/svelte/dist/style.css';
	import type { ResourceGraphSnapshot } from '$lib/api';
	import type { FlareResourceNode, FlareProducerNode } from './types';
	import { layoutGraph } from './layout';
	import ResourceNode from './ResourceNode.svelte';
	import ProducerNode from './ProducerNode.svelte';

	// showResourceNodes hides/shows the Docker-managed nodes (ClickHouse/Redis/ingest/
	// api/dashboard/docker-proxy) - producer nodes are unaffected either way, since
	// they're a separate concern (see types.ts's remarks). Shown by default: the whole
	// point of this page is Flare's own containers, producers are the add-on.
	let { snapshot, showResourceNodes = true }: { snapshot: ResourceGraphSnapshot; showResourceNodes?: boolean } = $props();

	const nodeTypes = { 'flare-resource': ResourceNode, 'flare-producer': ProducerNode };

	// Docker-managed nodes key by role (e.g. "clickhouse"), stable across polls and
	// matching ResourceEdgeDto.sourceRole/targetRole, unlike the underlying container ID
	// (not stable across a recreate). Producer nodes key by ProducerServiceDto.id - already
	// namespaced "service:<name>" server-side, so the two id spaces never collide and can
	// be laid out/rendered as one mixed array. Recomputed wholesale on every snapshot -
	// see layoutGraph's remarks for why that's fine here.
	let nodes = $state.raw<(FlareResourceNode | FlareProducerNode)[]>([]);
	let edges = $state.raw<Edge[]>([]);

	$effect(() => {
		const resourceNodes: FlareResourceNode[] = showResourceNodes
			? snapshot.nodes.map((node) => ({
					id: node.role,
					type: 'flare-resource',
					data: { node },
					position: { x: 0, y: 0 }
				}))
			: [];
		const producerNodes: FlareProducerNode[] = snapshot.producers.map((producer) => ({
			id: producer.id,
			type: 'flare-producer',
			data: { producer },
			position: { x: 0, y: 0 }
		}));

		// Dropping resource nodes above would otherwise leave dangling edges (a
		// Reference edge between two hidden roles, or a Producer edge into a now-hidden
		// "ingest") - SvelteFlow silently no-ops an edge with a missing endpoint rather
		// than erroring, but filtering explicitly here is clearer than relying on that.
		const visibleIds = new Set([...resourceNodes, ...producerNodes].map((n) => n.id));
		const rawEdges: Edge[] = snapshot.edges
			.filter((edge) => visibleIds.has(edge.sourceRole) && visibleIds.has(edge.targetRole))
			.map((edge) => ({
				id: `${edge.sourceRole}->${edge.targetRole}`,
				source: edge.sourceRole,
				target: edge.targetRole,
				label: edge.relationshipType,
				animated: true
			}));

		nodes = layoutGraph<FlareResourceNode | FlareProducerNode>([...resourceNodes, ...producerNodes], rawEdges);
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
