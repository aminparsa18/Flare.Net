<script lang="ts">
	// One panel's card chrome (title, type badge, drag handle, remove) around whichever
	// panels/Dashboard*PanelBody.svelte matches its `panelType`. Positioned/sized by the
	// parent DashboardGrid.svelte (gridstack) rather than by anything in here - this
	// component only fills whatever cell it's given (`h-full` below), see
	// docs-internal/adr/0024-custom-dashboards-phase2-editor.md.
	//
	// The drag handle, title-edit affordance, and remove button are all scoped to
	// `editing` - outside edit mode a panel is read-only chrome, so nothing here risks an
	// accidental drag/rename/delete while just looking at a dashboard.
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Spinner } from '$lib/components/ui/spinner';
	import DashboardLogsPanelBody from './panels/DashboardLogsPanelBody.svelte';
	import DashboardMetricsPanelBody from './panels/DashboardMetricsPanelBody.svelte';
	import DashboardTracesPanelBody from './panels/DashboardTracesPanelBody.svelte';
	import type { DashboardPanel } from '$lib/dashboards-api';
	import type { TimeRangePreset } from '$lib/logs/time-range';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import GripVerticalIcon from '@lucide/svelte/icons/grip-vertical';
	import * as m from '$lib/paraglide/messages';

	let {
		panel,
		editing,
		timeRangeOverride,
		removing,
		onRemove,
		onRename
	}: {
		panel: DashboardPanel;
		editing: boolean;
		timeRangeOverride: TimeRangePreset | null;
		removing: boolean;
		onRemove: () => void;
		onRename: (title: string) => void;
	} = $props();

	function panelTypeLabel(panelType: DashboardPanel['panelType']): string {
		switch (panelType) {
			case 'Logs':
				return m.nav_logs();
			case 'Traces':
				return m.nav_traces();
			case 'Metrics':
				return m.nav_metrics();
		}
	}

	function handleRemove(): void {
		if (!confirm(m.dashboardViewer_confirmRemovePanel({ title: panel.title }))) return;
		onRemove();
	}

	// Click-to-edit title, same "local draft, commit on blur/Enter" shape LogsToolbar's
	// search box uses - not a two-way bind straight to `panel.title`, since every commit
	// is a full dashboard PUT (DashboardViewerState.renamePanel), not something that
	// should fire per keystroke.
	let renaming = $state(false);
	// Always (re)seeded from `panel.title` by startRename() below right before use - this
	// starting value is never actually shown (the input only renders while `renaming` is
	// true), so it doesn't need to track `panel.title` itself.
	let titleDraft = $state('');

	function startRename(): void {
		if (!editing) return;
		titleDraft = panel.title;
		renaming = true;
	}

	function commitRename(): void {
		renaming = false;
		const next = titleDraft.trim();
		if (next && next !== panel.title) onRename(next);
	}

	function cancelRename(): void {
		renaming = false;
		titleDraft = panel.title;
	}
</script>

<div class="flex h-full flex-col rounded-lg border">
	<div class="flex items-center justify-between gap-2 border-b px-3 py-2">
		<div class="flex min-w-0 flex-1 items-center gap-2">
			{#if editing}
				<span class="panel-drag-handle text-muted-foreground hover:text-foreground shrink-0 cursor-grab" title={m.dashboardPanelCard_dragHandle()}>
					<GripVerticalIcon class="size-4" />
				</span>
			{/if}
			{#if renaming}
				<Input
					class="h-7 text-sm"
					autofocus
					bind:value={titleDraft}
					aria-label={m.dashboardPanelCard_renameLabel()}
					onblur={commitRename}
					onkeydown={(e) => {
						if (e.key === 'Enter') commitRename();
						else if (e.key === 'Escape') cancelRename();
					}}
				/>
			{:else}
				<button type="button" class="truncate text-left text-sm font-medium" disabled={!editing} onclick={startRename}>
					{panel.title}
				</button>
			{/if}
			<Badge variant="outline" class="shrink-0">{panelTypeLabel(panel.panelType)}</Badge>
		</div>
		{#if editing}
			<Button
				variant="ghost"
				size="icon-sm"
				class="text-muted-foreground hover:text-destructive shrink-0"
				title={m.dashboardViewer_removePanel()}
				disabled={removing}
				onclick={handleRemove}
			>
				{#if removing}<Spinner class="size-3" />{:else}<Trash2Icon />{/if}
			</Button>
		{/if}
	</div>
	<div class="flex min-h-0 flex-1 flex-col overflow-hidden">
		{#if panel.panelType === 'Logs'}
			<DashboardLogsPanelBody query={panel.query} {timeRangeOverride} />
		{:else if panel.panelType === 'Metrics'}
			<DashboardMetricsPanelBody query={panel.query} {timeRangeOverride} />
		{:else if panel.panelType === 'Traces'}
			<DashboardTracesPanelBody query={panel.query} {timeRangeOverride} />
		{/if}
	</div>
</div>
