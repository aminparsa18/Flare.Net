import type { Node } from '@xyflow/svelte';
import type { ResourceNodeDto } from '$lib/api';

/** SvelteFlow node type for the Resources graph - `id` is `ResourceNodeDto.role` (stable across polls), not the underlying container ID (not stable across a recreate) - see `ResourceGraph.svelte`'s remarks. */
export type FlareResourceNode = Node<{ node: ResourceNodeDto }, 'flare-resource'>;
