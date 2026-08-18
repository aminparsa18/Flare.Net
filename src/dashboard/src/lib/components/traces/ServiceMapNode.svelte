<script lang="ts">
	// Same card shape as resources/ResourceNode.svelte (border/shadow, Handle on
	// left+right for the shared left-to-right dagre layout) - deliberately not reused
	// directly, since that component is typed to FlareResourceNode/ResourceNodeDto
	// (container state/health/urls), fields a trace's services don't have.
	import { Handle, Position, type NodeProps } from '@xyflow/svelte';
	import { Badge } from '$lib/components/ui/badge';
	import type { ServiceMapFlowNode } from '$lib/traces/service-map-flow-types';
	import NetworkIcon from '@lucide/svelte/icons/network';

	let { data }: NodeProps<ServiceMapFlowNode> = $props();
	const service = $derived(data.service);
</script>

<Handle type="target" position={Position.Left} />
<div class="bg-card text-card-foreground w-[200px] rounded-lg border p-3 shadow-sm">
	<div class="flex items-center gap-2">
		<NetworkIcon class="text-muted-foreground size-4 shrink-0" />
		<span class="truncate text-sm font-medium" title={service.service}>{service.service}</span>
	</div>
	<div class="mt-2 flex flex-wrap items-center gap-1">
		<Badge variant="outline">{service.spanCount} {service.spanCount === 1 ? 'span' : 'spans'}</Badge>
		{#if service.hasError}
			<Badge variant="destructive">Error</Badge>
		{/if}
	</div>
</div>
<Handle type="source" position={Position.Right} />
