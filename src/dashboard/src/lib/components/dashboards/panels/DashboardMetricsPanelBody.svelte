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
	import { onMount, untrack } from 'svelte';
	import { MetricsExplorerState } from '$lib/metrics/state.svelte';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import MetricChart from '$lib/components/metrics/MetricChart.svelte';
	import type { TimeRangePreset } from '$lib/logs/time-range';
	import type { ResolvedVariableOverrides } from '$lib/dashboards/variables';

	let {
		query,
		timeRangeOverride,
		variableOverrides,
		refreshToken
	}: {
		query: unknown;
		timeRangeOverride: TimeRangePreset | null;
		variableOverrides: ResolvedVariableOverrides;
		refreshToken: number;
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
		void explorer.runQuery();
	});
</script>

<MetricChart />
