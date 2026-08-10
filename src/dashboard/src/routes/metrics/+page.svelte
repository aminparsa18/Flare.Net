<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { MetricsExplorerState } from '$lib/metrics/state.svelte';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import MetricsToolbar from '$lib/components/metrics/MetricsToolbar.svelte';
	import MetricPicker from '$lib/components/metrics/MetricPicker.svelte';
	import MetricChart from '$lib/components/metrics/MetricChart.svelte';

	const explorer = metricsExplorerContext.set(new MetricsExplorerState());

	onMount(() => {
		void explorer.loadNames();
		void explorer.loadKnownServices();
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
