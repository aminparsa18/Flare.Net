<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { IndexingState } from '$lib/indexing/state.svelte';
	import { indexingContext } from '$lib/indexing/context';
	import IndexingToolbar from '$lib/components/indexing/IndexingToolbar.svelte';
	import IndexingSummaryTiles from '$lib/components/indexing/IndexingSummaryTiles.svelte';
	import IndexingClusterStatus from '$lib/components/indexing/IndexingClusterStatus.svelte';
	import IndexingStorageHealth from '$lib/components/indexing/IndexingStorageHealth.svelte';
	import IndexingGrowthChart from '$lib/components/indexing/IndexingGrowthChart.svelte';
	import IndexingTablesTable from '$lib/components/indexing/IndexingTablesTable.svelte';
	import IndexingQueryOptimization from '$lib/components/indexing/IndexingQueryOptimization.svelte';
	import IndexingSkipIndexesTable from '$lib/components/indexing/IndexingSkipIndexesTable.svelte';

	const indexing = indexingContext.set(new IndexingState());

	onMount(() => {
		void indexing.load();
	});

	onDestroy(() => {
		indexing.dispose();
	});
</script>

<svelte:head>
	<title>Flare — Indexing</title>
</svelte:head>

<div class="flex h-full flex-col">
	<IndexingToolbar />
	<div class="flex min-h-0 flex-1 flex-col overflow-auto">
		{#if indexing.error}
			<p class="text-destructive px-4 py-3 text-sm">{indexing.error}</p>
		{/if}
		<IndexingSummaryTiles />
		<IndexingClusterStatus />
		<IndexingStorageHealth />
		<IndexingGrowthChart />
		<IndexingTablesTable />
		<IndexingQueryOptimization />
		<IndexingSkipIndexesTable />
	</div>
</div>
