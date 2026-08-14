// SvelteFlow has no built-in auto-layout - a small @dagrejs/dagre helper (nodes/edges in,
// positioned nodes out), same shape a same-day discarded exploration of this exact feature
// used before (see docs/prompts/docker-resources-graph-prompt.md).

import dagre from '@dagrejs/dagre';
import { Position, type Edge } from '@xyflow/svelte';
import type { FlareResourceNode } from './types';

const NODE_WIDTH = 220;
const NODE_HEIGHT = 92;

/**
 * Lays out nodes left-to-right via dagre. Recomputed wholesale every time
 * `ResourceGraph.svelte` receives a fresh snapshot (every poller tick, ~3s) rather than
 * preserving any user-dragged positions across ticks - simplest correct thing for a graph
 * this small that re-arranges its own node/edge set on every tick anyway.
 */
export function layoutGraph(nodes: FlareResourceNode[], edges: Edge[]): FlareResourceNode[] {
	const graph = new dagre.graphlib.Graph();
	graph.setGraph({ rankdir: 'LR', nodesep: 32, ranksep: 96 });
	graph.setDefaultEdgeLabel(() => ({}));

	for (const node of nodes) {
		graph.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
	}
	for (const edge of edges) {
		graph.setEdge(edge.source, edge.target);
	}

	dagre.layout(graph);

	return nodes.map((node) => {
		const { x, y } = graph.node(node.id);
		return {
			...node,
			// dagre positions by center; SvelteFlow positions by top-left corner.
			position: { x: x - NODE_WIDTH / 2, y: y - NODE_HEIGHT / 2 },
			sourcePosition: Position.Right,
			targetPosition: Position.Left
		};
	});
}
