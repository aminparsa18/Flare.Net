// SvelteFlow-specific node type for the Service Map tab - kept separate from
// service-map.ts's own framework-agnostic `ServiceMapNode` (same split
// resources/types.ts keeps from its own graph-building logic).

import type { Node } from '@xyflow/svelte';
import type { ServiceMapNode } from './service-map';

/** `id` is the service name - unique within one trace's dependency graph. */
export type ServiceMapFlowNode = Node<{ service: ServiceMapNode }, 'service-map'>;
