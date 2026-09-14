<script lang="ts">
	// One panel's card chrome (title, type badge, drag handle, remove) around whichever
	// panels/Dashboard*PanelBody.svelte matches its `panelType`. Positioned/sized by the
	// parent DashboardGrid.svelte (gridstack) rather than by anything in here - this
	// component only fills whatever cell it's given (`h-full` below), see
	// docs-internal/adr/0024-custom-dashboards-phase2-editor.md.
	//
	// The drag handle, title-edit affordance, duplicate, remove button, and per-panel
	// variables popover are all scoped to `editing` - outside edit mode a panel is read-only
	// chrome, so nothing here risks an accidental drag/rename/duplicate/delete/opt-out while
	// just looking at a dashboard. Export is the one exception, available in both modes -
	// same "read-only, no reason to gate it" call DashboardTable.svelte's own per-dashboard
	// Export button already makes.
	import { goto } from '$app/navigation';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Spinner } from '$lib/components/ui/spinner';
	import DashboardLogsPanelBody from './panels/DashboardLogsPanelBody.svelte';
	import DashboardMetricsPanelBody from './panels/DashboardMetricsPanelBody.svelte';
	import DashboardTracesPanelBody from './panels/DashboardTracesPanelBody.svelte';
	import PanelVariablesPopover from './PanelVariablesPopover.svelte';
	import type { DashboardPanel, DashboardVariable } from '$lib/dashboards-api';
	import type { TimeRangePreset } from '$lib/logs/time-range';
	import type { LogsSavedViewState } from '$lib/logs/state.svelte';
	import type { MetricsSavedViewState } from '$lib/metrics/state.svelte';
	import { resolveVariableOverrides } from '$lib/dashboards/variables';
	import { buildAlertDeepLinkHref, type AlertPanelDraft } from '$lib/deep-links';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import GripVerticalIcon from '@lucide/svelte/icons/grip-vertical';
	import BellPlusIcon from '@lucide/svelte/icons/bell-plus';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import * as m from '$lib/paraglide/messages';

	let {
		panel,
		editing,
		timeRangeOverride,
		variables,
		variableValues,
		refreshToken,
		removing,
		onRemove,
		onRename,
		onDuplicate,
		onExport,
		onToggleVariable
	}: {
		panel: DashboardPanel;
		editing: boolean;
		timeRangeOverride: TimeRangePreset | null;
		variables: DashboardVariable[];
		variableValues: Record<string, string | null>;
		refreshToken: number;
		removing: boolean;
		onRemove: () => void;
		onRename: (title: string) => void;
		onDuplicate: () => void;
		onExport: () => void;
		onToggleVariable: (variableId: string, excluded: boolean) => void;
	} = $props();

	/** This panel's own effective overrides - `variables`/`variableValues` narrowed by
	 *  `panel.excludedVariableIds` (see `DashboardPanel.excludedVariableIds`'s own remarks).
	 *  Recomputed whenever any of those three change, same invalidation surface the old
	 *  dashboard-wide `resolvedVariableOverrides` derived had (`variables`/`variableValues`
	 *  are only ever reassigned by DashboardViewerState's own variable-mutating methods, and
	 *  `panel` keeps its object identity across an unrelated panel's edit - see
	 *  DashboardViewerState.updateLayout/renamePanel/removePanel). */
	const variableOverrides = $derived(resolveVariableOverrides(variables, variableValues, panel.excludedVariableIds ?? []));

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

	// "Create alert" - Traces has no alert condition kind to draft into (AlertConditionKind
	// is LogCount/MetricThreshold/ExceptionCount only, see ADR-0020/ADR-0022), so this is
	// Logs/Metrics only. Reads the panel's *saved* query (the same `LogsSavedViewState`/
	// `MetricsSavedViewState` shape DashboardLogsPanelBody/DashboardMetricsPanelBody feed
	// into `explorer.applySavedViewState`), not the live in-panel explorer's own possibly
	// since-changed state - same "defensively narrowed, unknown at rest" caveat those
	// `applySavedViewState` methods already document. Available in both view and edit mode -
	// unlike rename/remove/drag, drafting an alert never risks losing anything, so there's
	// no reason to gate it behind `editing`.
	const alertDraft = $derived.by((): AlertPanelDraft | null => {
		if (panel.panelType === 'Logs') {
			const q = (panel.query ?? {}) as Partial<LogsSavedViewState>;
			return {
				kind: 'LogCount',
				name: m.dashboardPanelCard_alertNameFromPanel({ title: panel.title }),
				services: q.services ?? [],
				severityNumbers: q.severityNumbers ?? [],
				search: q.search ?? ''
			};
		}
		if (panel.panelType === 'Metrics') {
			const q = (panel.query ?? {}) as Partial<MetricsSavedViewState>;
			if (!q.selectedMetric) return null;
			return {
				kind: 'MetricThreshold',
				name: m.dashboardPanelCard_alertNameFromPanel({ title: panel.title }),
				metricName: q.selectedMetric.metricName,
				metricType: q.selectedMetric.type
			};
		}
		return null;
	});

	function handleCreateAlert(): void {
		if (alertDraft) void goto(buildAlertDeepLinkHref(alertDraft));
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
		{#if alertDraft}
			<Button
				variant="ghost"
				size="icon-sm"
				class="text-muted-foreground hover:text-foreground shrink-0"
				title={m.dashboardPanelCard_createAlert()}
				onclick={handleCreateAlert}
			>
				<BellPlusIcon />
			</Button>
		{/if}
		<Button
			variant="ghost"
			size="icon-sm"
			class="text-muted-foreground hover:text-foreground shrink-0"
			title={m.dashboardPanelCard_export()}
			onclick={onExport}
		>
			<DownloadIcon />
		</Button>
		{#if editing}
			{#if variables.length > 0}
				<PanelVariablesPopover {variables} excludedVariableIds={panel.excludedVariableIds} onToggle={onToggleVariable} />
			{/if}
			<Button
				variant="ghost"
				size="icon-sm"
				class="text-muted-foreground hover:text-foreground shrink-0"
				title={m.dashboardPanelCard_duplicate()}
				onclick={onDuplicate}
			>
				<CopyIcon />
			</Button>
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
			<DashboardLogsPanelBody query={panel.query} {timeRangeOverride} {variableOverrides} {refreshToken} />
		{:else if panel.panelType === 'Metrics'}
			<DashboardMetricsPanelBody query={panel.query} {timeRangeOverride} {variableOverrides} {refreshToken} />
		{:else if panel.panelType === 'Traces'}
			<DashboardTracesPanelBody query={panel.query} {timeRangeOverride} {variableOverrides} {refreshToken} />
		{/if}
	</div>
</div>
