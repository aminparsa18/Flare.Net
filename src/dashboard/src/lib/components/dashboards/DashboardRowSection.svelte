<script lang="ts">
	// One named, collapsible row on a dashboard (roadmap's "Collapsible rows / panel groups
	// on dashboards" item, see docs-internal/adr/0054-dashboard-collapsible-rows.md): a
	// header, then the row's own DashboardGrid (passed in as `children`). Collapsed, the
	// grid isn't rendered at all, so none of the row's panels mount or run a query; the
	// header shows how many panels are hidden. Expanding mounts them, and each still waits
	// for its own `use:inViewport` before querying, same as any other panel.
	//
	// Rename/reorder/remove/add-panel are scoped to `editing`, same as every panel
	// affordance in DashboardPanelCard.svelte. Collapsing works in both modes - see
	// DashboardViewerState.toggleRowCollapsed for when that's saved.
	import type { Snippet } from 'svelte';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import type { DashboardRow } from '$lib/dashboards-api';
	import ChevronRightIcon from '@lucide/svelte/icons/chevron-right';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import * as m from '$lib/paraglide/messages';

	let {
		row,
		panelCount,
		collapsed,
		editing,
		isFirst,
		isLast,
		onToggle,
		onRename,
		onMove,
		onRemove,
		onAddPanel,
		children
	}: {
		row: DashboardRow;
		panelCount: number;
		collapsed: boolean;
		editing: boolean;
		isFirst: boolean;
		isLast: boolean;
		onToggle: () => void;
		onRename: (title: string) => void;
		onMove: (direction: -1 | 1) => void;
		onRemove: () => void;
		onAddPanel: () => void;
		children: Snippet;
	} = $props();

	// Same "local draft, commit on blur/Enter" rename shape as DashboardPanelCard's title.
	let renaming = $state(false);
	let titleDraft = $state('');

	function startRename(): void {
		titleDraft = row.title;
		renaming = true;
	}

	function commitRename(): void {
		renaming = false;
		const next = titleDraft.trim();
		if (next && next !== row.title) onRename(next);
	}

	function handleRemove(): void {
		if (!confirm(m.dashboardRow_confirmRemove({ title: row.title }))) return;
		onRemove();
	}
</script>

<section class="mt-4">
	<div class="flex items-center gap-2 border-b pb-1">
		<Button
			variant="ghost"
			size="icon-sm"
			class="shrink-0"
			aria-expanded={!collapsed}
			title={collapsed ? m.dashboardRow_expand() : m.dashboardRow_collapse()}
			onclick={onToggle}
		>
			{#if collapsed}<ChevronRightIcon />{:else}<ChevronDownIcon />{/if}
		</Button>
		{#if renaming}
			<Input
				class="h-7 max-w-sm text-sm"
				autofocus
				bind:value={titleDraft}
				aria-label={m.dashboardRow_titleLabel()}
				onblur={commitRename}
				onkeydown={(e) => {
					if (e.key === 'Enter') commitRename();
					else if (e.key === 'Escape') renaming = false;
				}}
			/>
		{:else if editing}
			<button type="button" class="truncate text-left text-sm font-semibold" title={m.dashboardRow_rename()} onclick={startRename}>{row.title}</button>
		{:else}
			<button type="button" class="truncate text-left text-sm font-semibold" onclick={onToggle}>{row.title}</button>
		{/if}
		{#if collapsed}
			<Badge variant="secondary" class="shrink-0" title={m.dashboardRow_panelCountTitle({ count: panelCount })}>{panelCount}</Badge>
		{/if}

		{#if editing}
			<div class="ml-auto flex items-center gap-1">
				<Button variant="ghost" size="icon-sm" class="text-muted-foreground hover:text-foreground" title={m.dashboardRow_addPanel()} onclick={onAddPanel}>
					<PlusIcon />
				</Button>
				<Button variant="ghost" size="icon-sm" class="text-muted-foreground hover:text-foreground" title={m.dashboardRow_moveUp()} disabled={isFirst} onclick={() => onMove(-1)}>
					<ArrowUpIcon />
				</Button>
				<Button variant="ghost" size="icon-sm" class="text-muted-foreground hover:text-foreground" title={m.dashboardRow_moveDown()} disabled={isLast} onclick={() => onMove(1)}>
					<ArrowDownIcon />
				</Button>
				<Button variant="ghost" size="icon-sm" class="text-muted-foreground hover:text-destructive" title={m.dashboardRow_remove()} onclick={handleRemove}>
					<Trash2Icon />
				</Button>
			</div>
		{/if}
	</div>

	{#if !collapsed}
		{#if panelCount === 0}
			<p class="text-muted-foreground px-2 py-4 text-xs">{m.dashboardRow_empty()}</p>
		{:else}
			<div class="pt-1">
				{@render children()}
			</div>
		{/if}
	{/if}
</section>
