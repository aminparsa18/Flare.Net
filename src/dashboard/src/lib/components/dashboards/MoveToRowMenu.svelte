<script lang="ts">
	// Per-panel "Move to row" menu (roadmap's "Collapsible rows / panel groups on
	// dashboards" item). Each row is its own gridstack grid (see
	// docs-internal/adr/0054-dashboard-collapsible-rows.md), so a panel can't be dragged
	// from one row into another - this menu is how a panel changes rows instead. Only
	// rendered by DashboardPanelCard.svelte while `editing` and the dashboard has rows.
	import * as DropdownMenu from '$lib/components/ui/dropdown-menu';
	import { Button } from '$lib/components/ui/button';
	import type { DashboardRow } from '$lib/dashboards-api';
	import RowsIcon from '@lucide/svelte/icons/rows-3';
	import * as m from '$lib/paraglide/messages';

	let {
		rows,
		rowId,
		onMove
	}: {
		rows: DashboardRow[];
		/** The panel's current row, or `null` for the ungrouped area. */
		rowId: string | null;
		onMove: (rowId: string | null) => void;
	} = $props();

	// bits-ui's RadioGroup value is a string, so the ungrouped area (`null`) needs a sentinel
	// that can't collide with a row's UUID.
	const UNGROUPED = '__ungrouped__';
</script>

<DropdownMenu.Root>
	<DropdownMenu.Trigger>
		{#snippet child({ props })}
			<Button {...props} variant="ghost" size="icon-sm" class="text-muted-foreground hover:text-foreground shrink-0" title={m.dashboardPanelCard_moveToRow()}>
				<RowsIcon />
			</Button>
		{/snippet}
	</DropdownMenu.Trigger>
	<DropdownMenu.Content class="w-56" align="end">
		<DropdownMenu.Label>{m.dashboardPanelCard_moveToRow()}</DropdownMenu.Label>
		<DropdownMenu.RadioGroup value={rowId ?? UNGROUPED} onValueChange={(v) => v && onMove(v === UNGROUPED ? null : v)}>
			<DropdownMenu.RadioItem value={UNGROUPED}>{m.dashboardPanelCard_moveToNoRow()}</DropdownMenu.RadioItem>
			{#each rows as row (row.id)}
				<DropdownMenu.RadioItem value={row.id}><span class="truncate">{row.title}</span></DropdownMenu.RadioItem>
			{/each}
		</DropdownMenu.RadioGroup>
	</DropdownMenu.Content>
</DropdownMenu.Root>
