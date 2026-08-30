<script lang="ts">
	import { Handle, Position, type NodeProps } from '@xyflow/svelte';
	import type { FlareServiceNode } from './types';
	import RouteIcon from '@lucide/svelte/icons/route';

	let { data }: NodeProps<FlareServiceNode> = $props();
	const node = $derived(data.node);
</script>

<Handle type="target" position={Position.Left} />
<div class="bg-card text-card-foreground w-[220px] rounded-lg border border-dashed p-3 shadow-sm">
	<div class="flex items-center gap-2">
		<RouteIcon class="text-muted-foreground size-4 shrink-0" />
		<span class="truncate text-sm font-medium" title={node.name}>{node.name}</span>
	</div>
	<!-- border-dashed - same "observed rather than Flare-managed" visual convention
	     ProducerNode.svelte uses: a Kubernetes Service is a real cluster object, but not one
	     flare.role-labeled the way Pods are (see KubernetesResourcePoller's remarks), so it
	     reads more like an overlay than a core graph node. No state/health badge for the same
	     reason NamespaceNode.svelte has none - "Selects" edges (rendered by ResourceGraph.svelte)
	     show which Pods it actually routes to. -->
	<div class="text-muted-foreground mt-0.5 text-xs">service</div>
</div>
<Handle type="source" position={Position.Right} />
