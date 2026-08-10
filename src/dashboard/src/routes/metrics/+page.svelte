<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { MetricsExplorerState } from '$lib/metrics/state.svelte';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import { resolveRequestedSavedView } from '$lib/saved-views/hydrate';
	import MetricsToolbar from '$lib/components/metrics/MetricsToolbar.svelte';
	import MetricPicker from '$lib/components/metrics/MetricPicker.svelte';
	import MetricChart from '$lib/components/metrics/MetricChart.svelte';

	const explorer = metricsExplorerContext.set(new MetricsExplorerState());

	onMount(() => {
		void (async () => {
			// ?view=<id> (a saved view's shareable link) takes priority - applySavedViewState
			// already loads names + re-selects the saved metric itself, so loadNames() below
			// is only reached with no (or an invalid) view id.
			const view = await resolveRequestedSavedView(page.url, 'Metrics');
			if (view) {
				await explorer.applySavedViewState(view.state);
			} else {
				void explorer.loadNames();
			}
			void explorer.loadKnownServices();
		})();
	});

	onDestroy(() => {
		explorer.dispose();
	});
</script>

<svelte:head>
	<title>Flare — Metrics</title>
</svelte:head>

<div class="flex h-full flex-col">
	<MetricsToolbar />
	<div class="flex min-h-0 flex-1">
		<MetricPicker />
		<MetricChart />
	</div>
</div>
