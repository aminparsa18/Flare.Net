<script lang="ts">
	import { Handle, Position, type NodeProps } from '@xyflow/svelte';
	import type { FlareNamespaceNode } from './types';
	import FolderTreeIcon from '@lucide/svelte/icons/folder-tree';

	let { data }: NodeProps<FlareNamespaceNode> = $props();
	const node = $derived(data.node);
</script>

<Handle type="target" position={Position.Left} />
<div class="bg-card text-card-foreground w-[220px] rounded-lg border p-3 shadow-sm">
	<div class="flex items-center gap-2">
		<FolderTreeIcon class="text-muted-foreground size-4 shrink-0" />
		<span class="truncate text-sm font-medium" title={node.name}>{node.name}</span>
	</div>
	<!-- No state/health badge - unlike ResourceNode.svelte's container/Pod nodes, this is a
	     synthetic root the Kubernetes provider always emits, not a polled object with a real
	     lifecycle state - see ResourceGraph.svelte's remarks. -->
	<div class="text-muted-foreground mt-0.5 text-xs">namespace</div>
</div>
<Handle type="source" position={Position.Right} />
