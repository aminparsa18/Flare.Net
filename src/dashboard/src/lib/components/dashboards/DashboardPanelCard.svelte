<script lang="ts">
	// One panel's card chrome (title, type badge, drag handle, remove) around whichever
	// panels/Dashboard*PanelBody.svelte matches its `panelType`. Positioned/sized by the
	// parent DashboardGrid.svelte (gridstack) rather than by anything in here - this
	// component only fills whatever cell it's given (`h-full` below), see
	// docs-internal/adr/0024-custom-dashboards-phase2-editor.md.
	//
	// The drag handle, title-edit affordance, description editor, duplicate, remove button, per-panel variables
	// popover, and "Move to row" menu are all scoped to `editing` - outside edit mode a panel is read-only
	// chrome, so nothing here risks an accidental drag/rename/duplicate/delete/opt-out while
	// just looking at a dashboard. Export is the one exception, available in both modes -
	// same "read-only, no reason to gate it" call DashboardTable.svelte's own per-dashboard
	// Export button already makes.
	import { goto } from '$app/navigation';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { inViewport } from '$lib/actions/in-viewport';
	import DashboardLogsPanelBody from './panels/DashboardLogsPanelBody.svelte';
	import DashboardMetricsPanelBody from './panels/DashboardMetricsPanelBody.svelte';
	import DashboardTracesPanelBody from './panels/DashboardTracesPanelBody.svelte';
	import PanelVariablesPopover from './PanelVariablesPopover.svelte';
	import YAxisBoundsPopover from './YAxisBoundsPopover.svelte';
	import ThresholdsPopover from './ThresholdsPopover.svelte';
	import MoveToRowMenu from './MoveToRowMenu.svelte';
	import PanelDescriptionPopover from './PanelDescriptionPopover.svelte';
	import PanelTitleInput from './PanelTitleInput.svelte';
	import VisualizationMenu from './VisualizationMenu.svelte';
	import ColumnUnitsPopover from './ColumnUnitsPopover.svelte';
	import LegendPopover from './LegendPopover.svelte';
	import { effectivePanelYAxisScale, parseVisualization, usesLegend, usesYAxis, usesYAxisScale, type PanelReducer, type PanelVisualization } from '$lib/dashboards/visualization';
	import type { YAxisScale } from '$lib/metrics/axis';
	import type { PanelThreshold, ThresholdColor } from '$lib/dashboards/thresholds';
	import { parseLegendPosition, parseSeriesColors, type LegendPosition } from '$lib/dashboards/legend';
	import type { DashboardPanel, DashboardRow, DashboardVariable } from '$lib/dashboards-api';
	import type { TimeRangePreset } from '$lib/logs/time-range';
	import type { LogsSavedViewState } from '$lib/logs/state.svelte';
	import type { MetricsSavedViewState } from '$lib/metrics/state.svelte';
	import { resolvePanelTitle, resolveVariableOverrides } from '$lib/dashboards/variables';
	import { buildAlertDeepLinkHref, type AlertPanelDraft } from '$lib/deep-links';
	import { panelExplorerHref, panelExplorerState, withCustomRange, withLogsGroup } from '$lib/dashboards/explore-links';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import TelescopeIcon from '@lucide/svelte/icons/telescope';
	import GripVerticalIcon from '@lucide/svelte/icons/grip-vertical';
	import BellPlusIcon from '@lucide/svelte/icons/bell-plus';
	import CopyIcon from '@lucide/svelte/icons/copy';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import InfoIcon from '@lucide/svelte/icons/info';
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
		onSetDescription,
		onDuplicate,
		onExport,
		onToggleVariable,
		onSetYAxisBounds,
		onSetThresholds,
		onSetVisualization,
		onSetColumnUnits,
		onSetLegend,
		rows,
		rowId,
		onMoveToRow
	}: {
		panel: DashboardPanel;
		editing: boolean;
		timeRangeOverride: TimeRangePreset | null;
		variables: DashboardVariable[];
		variableValues: Record<string, string[]>;
		refreshToken: number;
		removing: boolean;
		onRemove: () => void;
		onRename: (title: string) => void;
		onSetDescription: (description: string) => void;
		onDuplicate: () => void;
		onExport: () => void;
		onToggleVariable: (variableId: string, excluded: boolean) => void;
		onSetYAxisBounds: (min: number | null, max: number | null, scale: YAxisScale) => void;
		onSetThresholds: (thresholds: PanelThreshold[]) => void;
		onSetVisualization: (visualization: PanelVisualization, reducer: PanelReducer | null) => void;
		onSetColumnUnits: (columnUnits: Partial<Record<PanelReducer, string>>) => void;
		onSetLegend: (legendPosition: LegendPosition | undefined, seriesColors: Record<string, ThresholdColor>) => void;
		rows: DashboardRow[];
		/** The row this panel currently sits in, or `null` for the ungrouped area. */
		rowId: string | null;
		onMoveToRow: (rowId: string | null) => void;
	} = $props();

	/** This panel's own effective overrides - `variables`/`variableValues` narrowed by
	 *  `panel.excludedVariableIds` (see `DashboardPanel.excludedVariableIds`'s own remarks).
	 *  Recomputed whenever any of those three change, same invalidation surface the old
	 *  dashboard-wide `resolvedVariableOverrides` derived had (`variables`/`variableValues`
	 *  are only ever reassigned by DashboardViewerState's own variable-mutating methods, and
	 *  `panel` keeps its object identity across an unrelated panel's edit - see
	 *  DashboardViewerState.updateLayout/renamePanel/removePanel). */
	const variableOverrides = $derived(resolveVariableOverrides(variables, variableValues, panel.excludedVariableIds ?? []));

	/** Lazy panel rendering (roadmap's "lazy-loading panels", signoz#2133) - the body below
	 *  (and its query) doesn't mount until this card has actually scrolled near the
	 *  viewport, via the `use:inViewport` below, instead of every panel firing its query on
	 *  page load regardless of whether it's ever seen. Once true, stays true - see
	 *  in-viewport.ts's own remarks on why this isn't a continuous show/hide. */
	let visible = $state(false);

	/** `panel.title` with its `$variable` references filled in from the current selection -
	 *  what's shown, and what a drafted alert / Table CSV download are named after. Not
	 *  narrowed by `excludedVariableIds`: that opt-out is about the panel's query, while a
	 *  title reference is something the author typed on purpose. */
	const displayTitle = $derived(resolvePanelTitle(panel.title, variables, variableValues, m.dashboardViewer_variableAll()));

	const visualization = $derived(parseVisualization(panel.visualization));
	const yAxisScale = $derived(effectivePanelYAxisScale(panel));
	const legendPosition = $derived(parseLegendPosition(panel.legendPosition));
	const seriesColors = $derived(parseSeriesColors(panel.seriesColors));

	/** The series the Metrics body last fetched (`seriesLabel` keys) - reported up from
	 *  DashboardMetricsPanelBody so LegendPopover can list them without owning the query. */
	let seriesKeys = $state<string[]>([]);

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
				name: m.dashboardPanelCard_alertNameFromPanel({ title: displayTitle }),
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
				name: m.dashboardPanelCard_alertNameFromPanel({ title: displayTitle }),
				metricName: q.selectedMetric.metricName,
				metricType: q.selectedMetric.type
			};
		}
		return null;
	});

	function handleCreateAlert(): void {
		if (alertDraft) void goto(buildAlertDeepLinkHref(alertDraft));
	}

	// "Open in Logs/Traces/Metrics" - the panel's query as it's currently shown (the
	// dashboard's time-range override and this panel's variable selections applied, see
	// $lib/dashboards/explore-links.ts), in its full explorer. A chart click opens the same
	// state narrowed to the clicked window (and, on a grouped Logs chart, that series). Both
	// modes, same "never risks losing anything" reasoning as "Create alert" above.
	const explorerState = $derived(panelExplorerState(panel, timeRangeOverride, variableOverrides));
	const explorerHref = $derived(panelExplorerHref(panel.panelType, explorerState));

	function explorerLabel(panelType: DashboardPanel['panelType']): string {
		switch (panelType) {
			case 'Logs':
				return m.dashboardPanelCard_openInLogs();
			case 'Traces':
				return m.dashboardPanelCard_openInTraces();
			case 'Metrics':
				return m.dashboardPanelCard_openInMetrics();
		}
	}

	function openRange(range: { from: Date; to: Date }, groupKey?: string | null): void {
		let state = withCustomRange(explorerState, range.from, range.to);
		if (groupKey !== undefined) state = withLogsGroup(state, groupKey);
		void goto(panelExplorerHref(panel.panelType, state));
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
				<PanelTitleInput bind:value={titleDraft} {variables} onCommit={commitRename} onCancel={cancelRename} />
			{:else}
				<button
					type="button"
					class="truncate text-left text-sm font-medium"
					disabled={!editing}
					title={displayTitle !== panel.title ? panel.title : undefined}
					onclick={startRename}
				>
					{displayTitle}
				</button>
			{/if}
			{#if panel.description}
				<!-- Shown in both modes - reading a panel's description is the whole point of
				     having one. Plain text (whitespace-pre-wrap keeps the author's line breaks),
				     never rendered as HTML. -->
				<Tooltip.Provider>
					<Tooltip.Root>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<span {...props} class="text-muted-foreground hover:text-foreground shrink-0" aria-label={m.dashboardPanelCard_description()}>
									<InfoIcon class="size-3.5" />
								</span>
							{/snippet}
						</Tooltip.Trigger>
						<Tooltip.Content class="max-w-xs whitespace-pre-wrap">{panel.description}</Tooltip.Content>
					</Tooltip.Root>
				</Tooltip.Provider>
			{/if}
			<Badge variant="outline" class="shrink-0">{panelTypeLabel(panel.panelType)}</Badge>
		</div>
		<Button
			variant="ghost"
			size="icon-sm"
			class="text-muted-foreground hover:text-foreground shrink-0"
			title={explorerLabel(panel.panelType)}
			aria-label={explorerLabel(panel.panelType)}
			href={explorerHref}
		>
			<TelescopeIcon />
		</Button>
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
			<PanelDescriptionPopover description={panel.description} onApply={onSetDescription} />
			{#if variables.length > 0}
				<PanelVariablesPopover {variables} excludedVariableIds={panel.excludedVariableIds} onToggle={onToggleVariable} />
			{/if}
			{#if panel.panelType === 'Metrics'}
				<VisualizationMenu {visualization} reducer={panel.reducer ?? null} onChange={onSetVisualization} />
				{#if usesYAxis(visualization)}
					<YAxisBoundsPopover
						yAxisMin={panel.yAxisMin}
						yAxisMax={panel.yAxisMax}
						{yAxisScale}
						showScale={usesYAxisScale(visualization)}
						onApply={onSetYAxisBounds}
					/>
				{/if}
				{#if visualization === 'table'}
					<ColumnUnitsPopover columnUnits={panel.columnUnits} onApply={onSetColumnUnits} />
				{/if}
				{#if usesLegend(visualization)}
					<LegendPopover {legendPosition} {seriesColors} {seriesKeys} onApply={onSetLegend} />
				{/if}
				<ThresholdsPopover thresholds={panel.thresholds} onApply={onSetThresholds} />
			{/if}
			{#if rows.length > 0}
				<MoveToRowMenu {rows} {rowId} onMove={onMoveToRow} />
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
	<div class="flex min-h-0 flex-1 flex-col overflow-hidden" use:inViewport={() => (visible = true)}>
		{#if visible}
			{#if panel.panelType === 'Logs'}
				<DashboardLogsPanelBody query={panel.query} {timeRangeOverride} {variableOverrides} {refreshToken} onOpenRange={openRange} />
			{:else if panel.panelType === 'Metrics'}
				<DashboardMetricsPanelBody
					query={panel.query}
					{timeRangeOverride}
					{variableOverrides}
					{refreshToken}
					yAxisMin={panel.yAxisMin}
					yAxisMax={panel.yAxisMax}
					{yAxisScale}
					thresholds={panel.thresholds}
					{visualization}
					reducer={panel.reducer}
					columnUnits={panel.columnUnits}
					{legendPosition}
					{seriesColors}
					bind:seriesKeys
					title={displayTitle}
					onOpenRange={openRange}
				/>
			{:else if panel.panelType === 'Traces'}
				<DashboardTracesPanelBody query={panel.query} {timeRangeOverride} {variableOverrides} {refreshToken} />
			{/if}
		{:else}
			<div class="flex h-full items-center justify-center"><Spinner /></div>
		{/if}
	</div>
</div>
