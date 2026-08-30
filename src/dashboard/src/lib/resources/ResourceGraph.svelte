<script lang="ts">
	import { SvelteFlow, Background, Controls, type Edge } from '@xyflow/svelte';
	import '@xyflow/svelte/dist/style.css';
	import type { ResourceGraphSnapshot } from '$lib/api';
	import type { FlareGraphNode, FlareTopologyNode, FlareProducerNode } from './types';
	import { layoutGraph } from './layout';
	import ResourceNode from './ResourceNode.svelte';
	import ProducerNode from './ProducerNode.svelte';
	import NamespaceNode from './NamespaceNode.svelte';
	import DeploymentGroupNode from './DeploymentGroupNode.svelte';
	import ServiceNode from './ServiceNode.svelte';

	// showResourceNodes hides/shows the topology-provider-managed nodes (Docker: ClickHouse/
	// Redis/ingest/api/dashboard/docker-proxy; Kubernetes: the Namespace/Deployment/Pod/
	// Service hierarchy) - producer nodes are unaffected either way, since they're a
	// separate concern (see types.ts's remarks). Shown by default: the whole point of this
	// page is Flare's own resources, producers are the add-on.
	let { snapshot, showResourceNodes = true }: { snapshot: ResourceGraphSnapshot; showResourceNodes?: boolean } = $props();

	// The three Kubernetes-only kinds map to their own node type - see types.ts's remarks
	// on why they're split out from 'flare-resource' rather than branching inside
	// ResourceNode.svelte. 'flare-resource' itself covers both Docker containers and
	// Kubernetes Pods - same ResourceNodeDto shape either way.
	const nodeTypes = {
		'flare-resource': ResourceNode,
		'flare-producer': ProducerNode,
		'flare-namespace': NamespaceNode,
		'flare-deployment': DeploymentGroupNode,
		'flare-k8s-service': ServiceNode
	};

	function nodeTypeFor(kind: string): 'flare-resource' | 'flare-namespace' | 'flare-deployment' | 'flare-k8s-service' {
		switch (kind) {
			case 'Namespace':
				return 'flare-namespace';
			case 'Deployment':
				return 'flare-deployment';
			case 'Service':
				return 'flare-k8s-service';
			default:
				return 'flare-resource'; // 'Container' (Docker) or 'Pod' (Kubernetes).
		}
	}

	// Every node kind keys by role (e.g. "clickhouse", or a Kubernetes provider's synthetic
	// "namespace:<name>"/"deployment:<role>"/"k8s-service:<name>" ids - see
	// ResourceNodeDto.role's remarks), stable across polls and matching
	// ResourceEdgeDto.sourceRole/targetRole, unlike the underlying container ID/Pod name
	// (not stable across a recreate). Producer nodes key by ProducerServiceDto.id - already
	// namespaced "service:<name>" server-side, so the two id spaces never collide and can
	// be laid out/rendered as one mixed array. Recomputed wholesale on every snapshot -
	// see layoutGraph's remarks for why that's fine here.
	let nodes = $state.raw<FlareGraphNode[]>([]);
	let edges = $state.raw<Edge[]>([]);

	$effect(() => {
		const resourceNodes: FlareTopologyNode[] = showResourceNodes
			? snapshot.nodes.map(
					(node): FlareTopologyNode => ({
						id: node.role,
						type: nodeTypeFor(node.kind),
						data: { node },
						position: { x: 0, y: 0 }
					})
				)
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
		const relationshipEdges: Edge[] = snapshot.edges
			.filter((edge) => visibleIds.has(edge.sourceRole) && visibleIds.has(edge.targetRole))
			.map((edge) => ({
				id: `${edge.sourceRole}->${edge.targetRole}`,
				source: edge.sourceRole,
				target: edge.targetRole,
				label: edge.relationshipType,
				animated: true
			}));

		// The Kubernetes provider's hierarchy is expressed as `parentId` on each node, not as
		// entries in snapshot.edges (see ResourceNodeDto.parentId's remarks) - derive one
		// synthetic, visually de-emphasized "Contains" edge per parent/child pair so the
		// hierarchy still renders as connected structure (Namespace → Deployment → Pod),
		// distinct from the labeled/animated Reference/Producer/Selects edges above. Always
		// empty for a Docker snapshot (no node ever has a parentId).
		const containsEdges: Edge[] = snapshot.nodes
			.filter((node) => node.parentId !== null && visibleIds.has(node.parentId) && visibleIds.has(node.role))
			.map((node) => ({
				id: `${node.parentId}~>${node.role}`,
				source: node.parentId!,
				target: node.role,
				animated: false,
				style: 'stroke-opacity:0.35'
			}));

		const rawEdges = [...containsEdges, ...relationshipEdges];

		nodes = layoutGraph<FlareGraphNode>([...resourceNodes, ...producerNodes], rawEdges);
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
