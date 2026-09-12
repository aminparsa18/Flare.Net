<script lang="ts">
	// Renders a "Metrics" panel by reusing the Metrics Explorer page's own state class +
	// chart wholesale - same reuse decision as DashboardLogsPanelBody.svelte, see that
	// file's header comment. `applySavedViewState` here is async (it reloads the metric
	// name list before re-selecting the saved metric - see MetricsExplorerState's own
	// remarks), so MetricChart's own `!explorer.selected` empty state carries the panel
	// until that resolves, rather than this component gating on it.
	import { onMount } from 'svelte';
	import { MetricsExplorerState } from '$lib/metrics/state.svelte';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import MetricChart from '$lib/components/metrics/MetricChart.svelte';

	let { query }: { query: unknown } = $props();

	const explorer = metricsExplorerContext.set(new MetricsExplorerState());

	onMount(() => {
		void explorer.applySavedViewState(query);
	});
</script>

<MetricChart />
