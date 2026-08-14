import type { Node } from '@xyflow/svelte';
import type { ResourceNodeDto, ProducerServiceDto } from '$lib/api';

/** SvelteFlow node type for a Flare-managed container - `id` is `ResourceNodeDto.role` (stable across polls), not the underlying container ID (not stable across a recreate) - see `ResourceGraph.svelte`'s remarks. */
export type FlareResourceNode = Node<{ node: ResourceNodeDto }, 'flare-resource'>;

/** SvelteFlow node type for an observed producer service (see `ProducerServiceDto`) - `id` is `ProducerServiceDto.id` (already namespaced `service:<name>` server-side, so it can never collide with a `FlareResourceNode`'s role id). */
export type FlareProducerNode = Node<{ producer: ProducerServiceDto }, 'flare-producer'>;
