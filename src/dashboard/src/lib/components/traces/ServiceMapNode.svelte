<script lang="ts">
	// Same card shape as resources/ResourceNode.svelte (border/shadow, Handle on
	// left+right for the shared left-to-right dagre layout) - deliberately not reused
	// directly, since that component is typed to FlareResourceNode/ResourceNodeDto
	// (container state/health/urls), fields a trace's services don't have.
	import { Handle, Position, type NodeProps } from '@xyflow/svelte';
	import { Badge } from '$lib/components/ui/badge';
	import type { ServiceMapFlowNode } from '$lib/traces/service-map-flow-types';
	import { formatDurationNano } from '$lib/traces/duration';
	import NetworkIcon from '@lucide/svelte/icons/network';
	import * as m from '$lib/paraglide/messages';

	let { data }: NodeProps<ServiceMapFlowNode> = $props();
	const service = $derived(data.service);

	// Capped rather than shown in full - a real service can rack up a long tail of
	// distinct operation names, and this card has ~220px to work with, same "truncate
	// rather than crowd out the rest of the card" instinct TraceWaterfall's row labels
	// already apply.
	const OPERATIONS_SHOWN = 3;
	const shownOperations = $derived(service.operations.slice(0, OPERATIONS_SHOWN));
	const hiddenOperationCount = $derived(service.operations.length - shownOperations.length);
</script>

<Handle type="target" position={Position.Left} />
<div class="bg-card text-card-foreground w-[220px] rounded-lg border p-3 shadow-sm">
	<div class="flex items-center gap-2">
		<NetworkIcon class="text-muted-foreground size-4 shrink-0" />
		<span class="truncate text-sm font-medium" title={service.service}>{service.service}</span>
	</div>
	<div class="mt-2 flex flex-wrap items-center gap-1">
		<Badge variant="outline">
			{service.spanCount === 1
				? m.serviceMapNode_spanSingular({ count: service.spanCount })
				: m.serviceMapNode_spanPlural({ count: service.spanCount })}
		</Badge>
		<Badge variant="outline">{formatDurationNano(service.totalDurationNano)}</Badge>
		{#if service.errorCount > 0}
			<Badge variant="destructive">
				{service.errorCount === 1
					? m.serviceMapNode_errorSingular({ count: service.errorCount })
					: m.serviceMapNode_errorPlural({ count: service.errorCount })}
			</Badge>
		{/if}
	</div>
	{#if shownOperations.length > 0}
		<div class="text-muted-foreground mt-2 flex flex-col gap-0.5 text-xs">
			{#each shownOperations as operation (operation)}
				<span class="truncate" title={operation}>{operation}</span>
			{/each}
			{#if hiddenOperationCount > 0}
				<span>{m.serviceMapNode_moreOperations({ count: hiddenOperationCount })}</span>
			{/if}
		</div>
	{/if}
</div>
<Handle type="source" position={Position.Right} />
