// SvelteFlow node type for the Ingestion topology diagram (Planning.md v10 follow-up:
// "understand where telemetry is flowing and where it is getting stuck"). One generic node
// shape (kind + title + a few label/value lines + optional badges) rather than a distinct
// component per stage - the five stages (receivers/stream/worker/storage/rejected) differ
// only in icon and content, not in layout, so a single TopologyNode.svelte renders all of
// them off this data instead of five near-identical components.

import type { Node } from '@xyflow/svelte';

export type TopologyNodeKind = 'receivers' | 'stream' | 'worker' | 'storage' | 'rejected';

/** Mirrors the warning/destructive severity vocabulary ingestion/health.ts already established for this page - "default" reads as the card's plain neutral styling, not a fourth color. */
export type TopologyTone = 'default' | 'warning' | 'destructive';

export type TopologyLine = {
	label: string;
	value: string;
	tone?: TopologyTone;
};

// The explicit index signature is required, not stylistic - @xyflow/svelte's
// Node<NodeData> constrains NodeData to Record<string, unknown>, which only a *named* type
// satisfies structurally by actually declaring one (an inline `{ ... }` written directly as
// a generic argument gets TS's implicit-index-signature inference for free; a named type
// referenced from elsewhere, like this one, doesn't).
export type TopologyNodeData = {
	kind: TopologyNodeKind;
	title: string;
	tone: TopologyTone;
	lines: TopologyLine[];
	badges?: string[];
	[key: string]: unknown;
};

export type IngestionTopologyNode = Node<TopologyNodeData, 'ingestion-topology'>;