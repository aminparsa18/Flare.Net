<script lang="ts">
	// Metrics catalog - every metric ingested in a window with its series count (cardinality),
	// sample volume, and last-received time, sortable to spot a cardinality explosion before
	// ClickHouse feels it. A sub-route of /metrics rather than a panel of the explorer: the
	// explorer charts one metric, this audits all of them.
	import { onMount, onDestroy } from 'svelte';
	import { MetricCatalogState } from '#lib/metric-catalog/state.svelte.ts';
	import { metricCatalogContext } from '#lib/metric-catalog/context.ts';
	import MetricCatalogToolbar from '#lib/components/metric-catalog/MetricCatalogToolbar.svelte';
	import MetricCatalogTable from '#lib/components/metric-catalog/MetricCatalogTable.svelte';
	import MetricAttributeRulesUnmatched from '#lib/components/metric-catalog/MetricAttributeRulesUnmatched.svelte';
	import { authContext } from '#lib/auth/context.ts';
	import MetricCatalogDetailSheet from '#lib/components/metric-catalog/MetricCatalogDetailSheet.svelte';
	import * as m from '#lib/paraglide/messages.js';
	import { startPageTour } from '#lib/tour/page.ts';
	import { catalogTour } from '#lib/tour/tours/catalog.ts';

	const auth = authContext.get();
	const catalog = metricCatalogContext.set(new MetricCatalogState());

	let stopTour: (() => void) | undefined;

	onMount(() => {
		stopTour = startPageTour(catalogTour);
		void catalog.load();
	});

	onDestroy(() => {
		stopTour?.();
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
	<div class="contents" data-tour="catalog-table"><MetricCatalogTable /></div>
	<MetricCatalogDetailSheet />
</div>
