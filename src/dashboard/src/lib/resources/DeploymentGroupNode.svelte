<script lang="ts">
	import { Handle, Position, type NodeProps } from '@xyflow/svelte';
	import type { FlareDeploymentNode } from './types';
	import LayersIcon from '@lucide/svelte/icons/layers';

	let { data }: NodeProps<FlareDeploymentNode> = $props();
	const node = $derived(data.node);
</script>

<Handle type="target" position={Position.Left} />
<div class="bg-card text-card-foreground w-[220px] rounded-lg border p-3 shadow-sm">
	<div class="flex items-center gap-2">
		<LayersIcon class="text-muted-foreground size-4 shrink-0" />
		<span class="truncate text-sm font-medium" title={node.name}>{node.name}</span>
	</div>
	<!-- "synthesized" is deliberate framing, not a hedge to hide: this groups Pods by their
	     flare.role label rather than reading the real Deployments API - see
	     KubernetesResourcePoller's remarks for why (RBAC scope trim). No state/health badge
	     for the same reason NamespaceNode.svelte has none. -->
	<div class="text-muted-foreground mt-0.5 text-xs">deployment (synthesized)</div>
</div>
<Handle type="source" position={Position.Right} />
