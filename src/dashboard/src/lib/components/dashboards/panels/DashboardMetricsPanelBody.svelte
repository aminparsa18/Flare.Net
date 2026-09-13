<script lang="ts">
	// Renders a "Metrics" panel by reusing the Metrics Explorer page's own state class +
	// chart wholesale - same reuse decision as DashboardLogsPanelBody.svelte, see that
	// file's header comment (including the `timeRangeOverride` handling, mirrored here).
	// `applySavedViewState` here is async (it reloads the metric name list before
	// re-selecting the saved metric - see MetricsExplorerState's own remarks), so
	// MetricChart's own `!explorer.selected` empty state carries the panel until that
	// resolves - `ready` below exists purely to sequence the override effect after the
	// initial saved-view application lands, not to gate what's rendered.
	// `refreshToken` (Phase 3) is DashboardViewerState's auto-refresh tick - see
	// DashboardLogsPanelBody.svelte's header comment for the general shape, mirrored here
	// against MetricsExplorerState.runQuery instead of runSearch.
	import { onMount, untrack } from 'svelte';
	import { MetricsExplorerState } from '$lib/metrics/state.svelte';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import MetricChart from '$lib/components/metrics/MetricChart.svelte';
	import type { TimeRangePreset } from '$lib/logs/time-range';

	let {
		query,
		timeRangeOverride,
		serviceOverride,
		refreshToken
	}: {
		query: unknown;
		timeRangeOverride: TimeRangePreset | null;
		serviceOverride: string | null;
		refreshToken: number;
	} = $props();

	const explorer = metricsExplorerContext.set(new MetricsExplorerState());
	let ready = $state(false);

	onMount(() => {
		void explorer.applySavedViewState(query).then(() => {
			ready = true;
		});
	});

	// See DashboardLogsPanelBody.svelte's identical field for why this isn't `$state` and
	// why it's seeded from the current prop via `untrack`.
	let overrideWasActive = untrack(() => timeRangeOverride != null);

	$effect(() => {
		const override = timeRangeOverride;
		if (!ready) return;
		// untrack: see DashboardLogsPanelBody.svelte's identical effect for why this whole
		// block must be untracked - explorer.setXxx() ends up (via loadNames/runQuery's own
		// synchronous prefix, before their first await) reading this same explorer's
		// `filter.*` fields, which would otherwise make this effect depend on state it just
		// wrote a moment earlier and loop (effect_update_depth_exceeded, caught live during
		// e2e verification on the Logs/serviceOverride case this mirrors).
		untrack(() => {
			if (override) {
				explorer.setTimeRangePreset(override);
			} else if (overrideWasActive) {
				// applySavedViewState is async here (unlike Logs/Traces - it reloads the metric
				// name list first) - a still-active serviceOverride must be reapplied only after
				// that resolves, or its own setServices would just be clobbered once the saved
				// state lands. See DashboardLogsPanelBody.svelte's identical branch for why this
				// reapply is needed at all.
				void explorer.applySavedViewState(query).then(() => {
					if (serviceOverride) explorer.setServices([serviceOverride]);
				});
			}
		});
		overrideWasActive = override != null;
	});

	// See DashboardLogsPanelBody.svelte's identical field/effect pair for the dashboard-wide
	// "Service" variable - mirrored here, with the same async-revert handling as the
	// timeRangeOverride effect above.
	let serviceOverrideWasActive = untrack(() => serviceOverride != null);

	$effect(() => {
		const override = serviceOverride;
		if (!ready) return;
		// See the timeRangeOverride effect above for why this must be untracked.
		untrack(() => {
			if (override) {
				explorer.setServices([override]);
			} else if (serviceOverrideWasActive) {
				void explorer.applySavedViewState(query).then(() => {
					if (timeRangeOverride) explorer.setTimeRangePreset(timeRangeOverride);
				});
			}
		});
		serviceOverrideWasActive = override != null;
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
