import type { Node } from '@xyflow/svelte';
import type { ResourceNodeDto, ProducerServiceDto } from '$lib/api';

/** SvelteFlow node type for a Flare-managed container (Docker) or Pod (Kubernetes) - `id` is `ResourceNodeDto.role` (stable across polls; for Kubernetes this is the `flare.role` label value, not the Pod's own name - see `ResourceNodeDto.role`'s remarks), not the underlying container ID/Pod name (not stable across a recreate) - see `ResourceGraph.svelte`'s remarks. */
export type FlareResourceNode = Node<{ node: ResourceNodeDto }, 'flare-resource'>;

/** SvelteFlow node type for an observed producer service (see `ProducerServiceDto`) - `id` is `ProducerServiceDto.id` (already namespaced `service:<name>` server-side, so it can never collide with a `FlareResourceNode`'s role id). */
export type FlareProducerNode = Node<{ producer: ProducerServiceDto }, 'flare-producer'>;

// The three Kubernetes-only node kinds below all carry the same `ResourceNodeDto` shape as
// FlareResourceNode (they're never emitted by the Docker provider - see `ResourceNodeDto.kind`'s
// remarks) - split into distinct SvelteFlow node types purely so each gets its own
// purpose-fitting rendering (NamespaceNode.svelte/DeploymentGroupNode.svelte/ServiceNode.svelte),
// the same "different type = different component" reasoning FlareProducerNode already uses
// against FlareResourceNode.

/** SvelteFlow node type for the Kubernetes provider's Namespace root - always exactly one per snapshot, `id`/`role` is `"namespace:<name>"`. */
export type FlareNamespaceNode = Node<{ node: ResourceNodeDto }, 'flare-namespace'>;

/** SvelteFlow node type for the Kubernetes provider's synthesized "Deployment" group (one per distinct `flare.role` among the listed Pods - not a live Deployments API read, see `KubernetesResourcePoller`'s remarks), `id`/`role` is `"deployment:<role>"`. */
export type FlareDeploymentNode = Node<{ node: ResourceNodeDto }, 'flare-deployment'>;

/** SvelteFlow node type for a Kubernetes Service that selects at least one Flare-labeled Pod, `id`/`role` is `"k8s-service:<name>"` (deliberately distinct from `FlareProducerNode`'s `"service:<name>"` prefix - see `ResourceNodeDto.cs`'s remarks). */
export type FlareServiceNode = Node<{ node: ResourceNodeDto }, 'flare-k8s-service'>;

/** Every kind that carries `data: { node: ResourceNodeDto }` (i.e. every node type except `FlareProducerNode`) - lets `ResourceGraph.svelte` build one node array with a dynamically-picked `type` per entry without TypeScript needing to discriminate on it (every member's `data` shape is identical). */
export type FlareTopologyNode = FlareResourceNode | FlareNamespaceNode | FlareDeploymentNode | FlareServiceNode;

/** Every SvelteFlow node type this page can render - the type `ResourceGraph.svelte`/`layout.ts` are generic over. */
export type FlareGraphNode = FlareTopologyNode | FlareProducerNode;
