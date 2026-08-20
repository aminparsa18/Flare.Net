<script lang="ts">
	// One generic renderer for every stage of the Ingestion topology diagram - see
	// topology-types.ts's remarks for why this is one component, not five.
	import { Handle, Position, type NodeProps } from '@xyflow/svelte';
	import { Badge } from '$lib/components/ui/badge';
	import type { IngestionTopologyNode, TopologyNodeKind, TopologyTone } from '$lib/ingestion/topology-types';
	import InboxIcon from '@lucide/svelte/icons/inbox';
	import ServerIcon from '@lucide/svelte/icons/server';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import DatabaseIcon from '@lucide/svelte/icons/database';
	import CircleAlertIcon from '@lucide/svelte/icons/circle-alert';

	let { data }: NodeProps<IngestionTopologyNode> = $props();

	// Same icon-per-role idea ResourceNode.svelte's ROLE_ICONS uses, and reuses two of its
	// exact icons (inbox/database) for the stages that are conceptually the same thing
	// (the ingest container, ClickHouse) so the two graphs read as one visual language.
	const KIND_ICON = {
		receivers: InboxIcon,
		stream: ServerIcon,
		worker: RefreshCwIcon,
		storage: DatabaseIcon,
		rejected: CircleAlertIcon
	} satisfies Record<TopologyNodeKind, typeof InboxIcon>;
	const Icon = $derived(KIND_ICON[data.kind]);

	const BORDER_CLASS = {
		default: 'border-border',
		warning: 'border-warning/60',
		destructive: 'border-destructive/60'
	} satisfies Record<TopologyTone, string>;

	const LINE_VALUE_CLASS = {
		default: '',
		warning: 'text-warning',
		destructive: 'text-destructive'
	} satisfies Record<TopologyTone, string>;
</script>

<Handle type="target" position={Position.Left} />
<div class="bg-card text-card-foreground w-[210px] rounded-lg border p-3 shadow-sm {BORDER_CLASS[data.tone]}">
	<div class="flex items-center gap-2">
		<Icon class="text-muted-foreground size-4 shrink-0" />
		<span class="truncate text-sm font-medium">{data.title}</span>
	</div>

	{#if data.lines.length > 0}
		<div class="mt-2 flex flex-col gap-0.5">
			{#each data.lines as line (line.label)}
				<div class="flex items-center justify-between gap-2 text-xs">
					<span class="text-muted-foreground truncate">{line.label}</span>
					<span class="shrink-0 tabular-nums {LINE_VALUE_CLASS[line.tone ?? 'default']}">{line.value}</span>
				</div>
			{/each}
		</div>
	{/if}

	{#if data.badges?.length}
		<div class="mt-2 flex flex-wrap gap-1">
			{#each data.badges as badge (badge)}
				<Badge variant="outline" class="text-[10px]">{badge}</Badge>
			{/each}
		</div>
	{/if}
</div>
<Handle type="source" position={Position.Right} />