<script lang="ts">
	// Renders a "Metrics" panel by reusing the Metrics Explorer page's own state class +
	// chart wholesale - same reuse decision as DashboardLogsPanelBody.svelte, see that
	// file's header comment (including the combined `timeRangeOverride`/`variableOverrides`
	// effect, mirrored here against the async `applySavedViewState`/`runQuery`).
	// `applySavedViewState` here is async (it reloads the metric name list before
	// re-selecting the saved metric - see MetricsExplorerState's own remarks), so
	// MetricChart's own `!explorer.selected` empty state carries the panel until that
	// resolves - `ready` below exists purely to sequence the override effect after the
	// initial saved-view application lands, not to gate what's rendered.
	//
	// Metrics has no attribute-equality filter of its own (only `services` and a single
	// `groupByAttributeKey` dimension - see MetricsFilterState's own remarks), so an
	// `Attribute`-target variable simply has nothing to attach to here: `variableOverrides`
	// still carries any resolved attribute values, this panel just never reads them, the
	// same way it already ignores every attribute-drill-down concept Logs/Traces have that
	// Metrics doesn't.
	//
	// `refreshToken` (Phase 3) is DashboardViewerState's auto-refresh tick - see
	// DashboardLogsPanelBody.svelte's header comment for the general shape, mirrored here
	// against MetricsExplorerState.runQuery instead of runSearch.
	//
	// MetricChart gets `allowZoom={false}` for the same reason DashboardLogsPanelBody passes
	// it to VolumeChart - see that file's comment and MetricChart.svelte's own remarks on
	// the prop.
	//
	// Formula-mode panels (docs-internal/adr/0037-dashboard-metrics-formula-panels.md,
	// closing the follow-up ADR-0036 left open) render FormulaChart instead - `explorer.mode`
	// is restored by applySavedViewState above, same source of truth the Explorer page's own
	// mode branch (routes/metrics/+page.svelte) reads.
	//
	// `visualization` (docs-internal/adr/0059-dashboard-panel-visualizations.md) picks how the
	// fetched result is drawn - the default `timeSeries` keeps the line charts above; every
	// other value hands the same explorer's result to MetricsVisualization instead. The
	// query path here is identical either way, so switching never re-runs anything.
	import { onMount, untrack } from 'svelte';
	import { MetricsExplorerState } from '$lib/metrics/state.svelte';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import MetricChart from '$lib/components/metrics/MetricChart.svelte';
	import FormulaChart from '$lib/components/metrics/FormulaChart.svelte';
	import type { TimeRangePreset } from '$lib/logs/time-range';
	import type { ResolvedVariableOverrides } from '$lib/dashboards/variables';
	import type { PanelThreshold } from '$lib/dashboards/thresholds';
	import type { PanelVisualization } from '$lib/dashboards/visualization';
	import MetricsVisualization from './visualizations/MetricsVisualization.svelte';

	let {
		query,
		timeRangeOverride,
		variableOverrides,
		refreshToken,
		yAxisMin,
		yAxisMax,
		thresholds,
		visualization = 'timeSeries',
		reducer,
		title = ''
	}: {
		query: unknown;
		timeRangeOverride: TimeRangePreset | null;
		variableOverrides: ResolvedVariableOverrides;
		refreshToken: number;
		/** This panel's own `DashboardPanel.yAxisMin`/`yAxisMax` - passed straight through to
		 *  whichever chart is active (MetricChart or FormulaChart), see their own
		 *  `domainMin`/`domainMax` remarks for how a soft bound is applied. */
		yAxisMin?: number | null;
		yAxisMax?: number | null;
		/** This panel's own `DashboardPanel.thresholds` - passed straight through the same way. */
		thresholds?: PanelThreshold[];
		/** This panel's own (already-parsed) `DashboardPanel.visualization`. */
		visualization?: PanelVisualization;
		/** This panel's own `DashboardPanel.reducer`, unvalidated - see MetricsVisualization. */
		reducer?: unknown;
		/** The panel's title - only used to name a Table visualization's CSV download. */
		title?: string;
	} = $props();

	const explorer = metricsExplorerContext.set(new MetricsExplorerState());
	let ready = $state(false);

	onMount(() => {
		void explorer.applySavedViewState(query).then(() => {
			ready = true;
		});
	});

	// See DashboardLogsPanelBody.svelte's identical effect for why this recomputes the
	// panel's whole effective filter from its saved baseline on every change instead of
	// reverting/reapplying overrides pairwise, and why the block below must be untracked -
	// explorer.setXxx()/applySavedViewState end up (via loadNames/runQuery's own synchronous
	// prefix, before their first await) reading this same explorer's `filter.*` fields,
	// which would otherwise make this effect depend on state it just wrote a moment earlier
	// and loop (effect_update_depth_exceeded, caught live during Phase 4's own e2e
	// verification).
	$effect(() => {
		const range = timeRangeOverride;
		const overrides = variableOverrides;
		if (!ready) return;
		untrack(() => {
			// applySavedViewState is async here (unlike Logs/Traces - it reloads the metric name
			// list first) - `services` must be applied only after that resolves, or setServices
			// would just be clobbered once the saved state lands.
			void explorer.applySavedViewState(query).then(() => {
				if (range) explorer.setTimeRangePreset(range);
				if (overrides.services.length) explorer.setServices(overrides.services);
				// Neither override above ran a query, so this panel must run its own. The
				// onMount apply's selectMetric only *scheduled* one (#deferredReset's timer),
				// and this second apply's leading #flushPendingSwitch cancels that timer
				// without running it - while its own selectMetric is a no-op (same metric) -
				// so a single-metric panel on a dashboard with no time-range override and no
				// applicable variable used to sit on "No data in range" forever. Formula mode
				// needs nothing here: applySavedViewState already runs runFormulaQuery itself.
				if (!range && !overrides.services.length && explorer.mode !== 'formula') void explorer.runQuery();
			});
		});
	});

	// See DashboardLogsPanelBody.svelte's identical block for why this compares against a
	// snapshot rather than reacting to every refreshToken value unconditionally.
	let lastRefreshToken = untrack(() => refreshToken);

	$effect(() => {
		const token = refreshToken;
		if (!ready || token === lastRefreshToken) return;
		lastRefreshToken = token;
		if (explorer.mode === 'formula') void explorer.runFormulaQuery();
		else void explorer.runQuery();
	});
</script>

{#if visualization !== 'timeSeries'}
	<MetricsVisualization {visualization} {reducer} {title} yAxisMin={yAxisMin ?? null} yAxisMax={yAxisMax ?? null} thresholds={thresholds ?? []} />
{:else if explorer.mode === 'formula'}
	<FormulaChart yAxisMin={yAxisMin ?? null} yAxisMax={yAxisMax ?? null} thresholds={thresholds ?? []} />
{:else}
	<MetricChart allowZoom={false} yAxisMin={yAxisMin ?? null} yAxisMax={yAxisMax ?? null} thresholds={thresholds ?? []} />
{/if}
