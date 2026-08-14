<script lang="ts">
	import { Handle, Position, type NodeProps } from '@xyflow/svelte';
	import { Badge } from '$lib/components/ui/badge';
	import type { ResourceState, ResourceHealth } from '$lib/api';
	import type { FlareResourceNode } from './types';
	import DatabaseIcon from '@lucide/svelte/icons/database';
	import ServerIcon from '@lucide/svelte/icons/server';
	import InboxIcon from '@lucide/svelte/icons/inbox';
	import NetworkIcon from '@lucide/svelte/icons/network';
	import LayoutDashboardIcon from '@lucide/svelte/icons/layout-dashboard';
	import BoxIcon from '@lucide/svelte/icons/box';
	import ExternalLinkIcon from '@lucide/svelte/icons/external-link';

	let { data }: NodeProps<FlareResourceNode> = $props();
	const node = $derived(data.node);

	// Known Flare roles get a purpose-fitting icon; anything else (e.g. the opt-in
	// docker-proxy sidecar itself, which also carries flare.resource=true - see
	// docker-compose.yml) falls back to a generic box rather than being hidden.
	const ROLE_ICONS: Record<string, typeof DatabaseIcon> = {
		clickhouse: DatabaseIcon,
		redis: ServerIcon,
		ingest: InboxIcon,
		api: NetworkIcon,
		dashboard: LayoutDashboardIcon
	};
	const Icon = $derived(ROLE_ICONS[node.role] ?? BoxIcon);

	/** Running+healthy reads as "good" (secondary), Exited/Unhealthy as "bad" (destructive) - everything transitional or unclear (Restarting/Paused/Unknown/Starting) stays neutral (outline), same three-way variant-by-status idea as LogsToolbar's live-tail connection badge. */
	function stateVariant(state: ResourceState): 'secondary' | 'destructive' | 'outline' {
		switch (state) {
			case 'Running':
				return 'secondary';
			case 'Exited':
				return 'destructive';
			default:
				return 'outline';
		}
	}

	function healthVariant(health: ResourceHealth): 'secondary' | 'destructive' | 'outline' {
		switch (health) {
			case 'Healthy':
				return 'secondary';
			case 'Unhealthy':
				return 'destructive';
			default:
				return 'outline'; // Starting.
		}
	}
</script>

<Handle type="target" position={Position.Left} />
<div class="bg-card text-card-foreground w-[220px] rounded-lg border p-3 shadow-sm">
	<div class="flex items-center gap-2">
		<Icon class="text-muted-foreground size-4 shrink-0" />
		<span class="truncate text-sm font-medium" title={node.name}>{node.name}</span>
	</div>
	<div class="text-muted-foreground mt-0.5 text-xs">{node.role}</div>
	<div class="mt-2 flex flex-wrap items-center gap-1">
		<Badge variant={stateVariant(node.state)}>{node.state}</Badge>
		{#if node.health}
			<Badge variant={healthVariant(node.health)}>{node.health}</Badge>
		{/if}
	</div>
	{#if node.urls.length > 0}
		<div class="mt-2 flex flex-col gap-1">
			{#each node.urls as url (url)}
				<a
					href={url}
					target="_blank"
					rel="noreferrer"
					class="text-primary flex items-center gap-1 truncate text-xs hover:underline"
				>
					<ExternalLinkIcon class="size-3 shrink-0" />
					<span class="truncate">{url}</span>
				</a>
			{/each}
		</div>
	{/if}
</div>
<Handle type="source" position={Position.Right} />
