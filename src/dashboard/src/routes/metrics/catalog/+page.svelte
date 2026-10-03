<script lang="ts">
	// Metrics catalog - every metric ingested in a window with its series count (cardinality),
	// sample volume, and last-received time, sortable to spot a cardinality explosion before
	// ClickHouse feels it. A sub-route of /metrics rather than a panel of the explorer: the
	// explorer charts one metric, this audits all of them.
	import { onMount, onDestroy } from 'svelte';
	import { MetricCatalogState } from '$lib/metric-catalog/state.svelte';
	import { metricCatalogContext } from '$lib/metric-catalog/context';
	import MetricCatalogToolbar from '$lib/components/metric-catalog/MetricCatalogToolbar.svelte';
	import MetricCatalogTable from '$lib/components/metric-catalog/MetricCatalogTable.svelte';
	import MetricAttributeRulesUnmatched from '$lib/components/metric-catalog/MetricAttributeRulesUnmatched.svelte';
	import { authContext } from '$lib/auth/context';
	import MetricCatalogDetailSheet from '$lib/components/metric-catalog/MetricCatalogDetailSheet.svelte';
	import * as m from '$lib/paraglide/messages';

	const auth = authContext.get();
	const catalog = metricCatalogContext.set(new MetricCatalogState());

	onMount(() => {
		void catalog.load();
	});

	onDestroy(() => {
		catalog.dispose();
	});
</script>

<svelte:head>
	<title>{m.metricCatalog_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<MetricCatalogToolbar />
	{#if auth.canMutate}
		<MetricAttributeRulesUnmatched />
	{/if}
	<MetricCatalogTable />
	<MetricCatalogDetailSheet />
</div>
