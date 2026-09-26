<script lang="ts">
	// A Metrics panel's non-line visualizations (DashboardPanel.visualization - see
	// `$lib/dashboards/visualization.ts`). Reads the same per-panel MetricsExplorerState
	// DashboardMetricsPanelBody already runs the query through - single-metric `series` or
	// Formula mode's `formulaSeries` - so switching visualization never changes or re-runs the
	// query, it only redraws the result already fetched. The loading/error/empty states mirror
	// MetricChart's/FormulaChart's own.
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import type { PanelThreshold } from '$lib/dashboards/thresholds';
	import { parseColumnUnits, reduceValues, resolveReducer, toVizSeries, totalsByBucket, type PanelVisualization } from '$lib/dashboards/visualization';
	import BarVisualization from './BarVisualization.svelte';
	import ValueVisualization from './ValueVisualization.svelte';
	import PieVisualization from './PieVisualization.svelte';
	import TableVisualization from './TableVisualization.svelte';
	import HistogramVisualization from './HistogramVisualization.svelte';
	import * as m from '$lib/paraglide/messages';

	let {
		visualization,
		reducer: rawReducer,
		title,
		yAxisMin = null,
		yAxisMax = null,
		thresholds = [],
		columnUnits: rawColumnUnits
	}: {
		visualization: Exclude<PanelVisualization, 'timeSeries'>;
		/** The panel's stored `reducer` - unvalidated; resolved against the result type below. */
		reducer: unknown;
		title: string;
		yAxisMin?: number | null;
		yAxisMax?: number | null;
		thresholds?: PanelThreshold[];
		/** The panel's stored `columnUnits` - unvalidated; only the Table reads it. */
		columnUnits?: unknown;
	} = $props();

	const explorer = metricsExplorerContext.get();
	const isFormula = $derived(explorer.mode === 'formula');

	// A formula's result is dimensionless and has no single point type (see FormulaChart's
	// own remarks) - its points carry a plain `value`, read the same way as a Gauge's.
	const resultType = $derived(isFormula ? null : explorer.resultType);
	const unit = $derived(isFormula ? null : (explorer.selected?.unit ?? null));
	const series = $derived(toVizSeries(isFormula ? explorer.formulaSeries : explorer.series, resultType));
	const reducer = $derived(resolveReducer(rawReducer, resultType));
	const columnUnits = $derived(parseColumnUnits(rawColumnUnits));
	const loading = $derived(isFormula ? explorer.formulaLoading : explorer.queryLoading);
	const error = $derived(isFormula ? explorer.formulaError : explorer.queryError);
	const hasData = $derived(series.some((s) => s.points.length > 0));

	const entries = $derived(
		series.map((s) => ({ label: s.displayLabel, value: reduceValues(s.points.map((p) => p.value), reducer) })).filter((e): e is { label: string; value: number } => e.value != null)
	);
	const total = $derived(reduceValues(totalsByBucket(series), reducer));
</script>

<div class="flex min-h-0 flex-1 flex-col overflow-hidden p-2">
	{#if !isFormula && !explorer.selected}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>{m.metricChart_noMetricSelectedTitle()}</Empty.Title>
				<Empty.Description>{m.metricChart_noMetricSelectedDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else if loading && !hasData}
		<div class="flex flex-1 items-center justify-center"><Spinner /></div>
	{:else if error}
		<p class="text-destructive p-2 text-xs">{error}</p>
	{:else if !hasData}
		<div class="text-muted-foreground flex flex-1 items-center justify-center text-xs">{m.metricChart_noDataInRange()}</div>
	{:else if visualization === 'bar' || visualization === 'stackedBar'}
		<BarVisualization {series} stacked={visualization === 'stackedBar'} {unit} {yAxisMin} {yAxisMax} {thresholds} />
	{:else if visualization === 'value' && total != null}
		<ValueVisualization value={total} {unit} {reducer} seriesCount={series.length} {thresholds} />
	{:else if visualization === 'pie'}
		<PieVisualization {entries} {unit} />
	{:else if visualization === 'table'}
		<TableVisualization {series} {unit} {columnUnits} {reducer} includeSum={resultType === 'Sum'} {title} {thresholds} />
	{:else if visualization === 'histogram'}
		<HistogramVisualization {series} {unit} {thresholds} />
	{/if}
</div>
