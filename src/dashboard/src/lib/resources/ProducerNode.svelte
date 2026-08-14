<script lang="ts">
	import { Handle, Position, type NodeProps } from '@xyflow/svelte';
	import { formatAge, secondsSince } from '$lib/ingestion/format';
	import type { FlareProducerNode } from './types';
	import ActivityIcon from '@lucide/svelte/icons/activity';

	let { data }: NodeProps<FlareProducerNode> = $props();
	const producer = $derived(data.producer);

	// Recomputed on every render rather than cached - this node re-renders whenever a
	// fresh snapshot arrives anyway (~3s), so the "ago" label stays live for free.
	const lastSeenLabel = $derived(formatAge(secondsSince(producer.lastSeenAt)));
</script>

<Handle type="target" position={Position.Left} />
<div class="bg-card text-card-foreground w-[220px] rounded-lg border border-dashed p-3 shadow-sm">
	<div class="flex items-center gap-2">
		<ActivityIcon class="text-muted-foreground size-4 shrink-0" />
		<span class="truncate text-sm font-medium" title={producer.serviceName}>{producer.serviceName}</span>
	</div>
	<div class="text-muted-foreground mt-0.5 text-xs">producer service</div>
	<div class="text-muted-foreground mt-2 text-xs">Last seen {lastSeenLabel}</div>
</div>
<Handle type="source" position={Position.Right} />
