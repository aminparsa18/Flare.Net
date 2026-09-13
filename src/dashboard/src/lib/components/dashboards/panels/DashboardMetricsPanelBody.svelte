<script lang="ts">
	// Renders a "Metrics" panel by reusing the Metrics Explorer page's own state class +
	// chart wholesale - same reuse decision as DashboardLogsPanelBody.svelte, see that
	// file's header comment (including the `timeRangeOverride` handling, mirrored here).
	// `applySavedViewState` here is async (it reloads the metric name list before
	// re-selecting the saved metric - see MetricsExplorerState's own remarks), so
	// MetricChart's own `!explorer.selected` empty state carries the panel until that
	// resolves - `ready` below exists purely to sequence the override effect after the
	// initial saved-view application lands, not to gate what's rendered.
	import { onMount, untrack } from 'svelte';
	import { MetricsExplorerState } from '$lib/metrics/state.svelte';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import MetricChart from '$lib/components/metrics/MetricChart.svelte';
	import type { TimeRangePreset } from '$lib/logs/time-range';

	let { query, timeRangeOverride }: { query: unknown; timeRangeOverride: TimeRangePreset | null } = $props();

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
		if (override) {
			explorer.setTimeRangePreset(override);
		} else if (overrideWasActive) {
			void explorer.applySavedViewState(query);
		}
		overrideWasActive = override != null;
	});
</script>

<MetricChart />
