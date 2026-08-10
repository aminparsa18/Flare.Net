<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { IngestionState } from '$lib/ingestion/state.svelte';
	import { ingestionContext } from '$lib/ingestion/context';
	import IngestionToolbar from '$lib/components/ingestion/IngestionToolbar.svelte';
	import IngestionTiles from '$lib/components/ingestion/IngestionTiles.svelte';
	import IngestionChart from '$lib/components/ingestion/IngestionChart.svelte';
	import IngestionSignalsTable from '$lib/components/ingestion/IngestionSignalsTable.svelte';
	import IngestionLog from '$lib/components/ingestion/IngestionLog.svelte';

	const ingestion = ingestionContext.set(new IngestionState());

	onMount(() => {
		void ingestion.load();
		ingestion.startPolling();
	});

	onDestroy(() => {
		ingestion.dispose();
	});
</script>

<svelte:head>
	<title>Flare — Ingestion</title>
</svelte:head>

<div class="flex h-full flex-col">
	<IngestionToolbar />
	<div class="flex min-h-0 flex-1 flex-col overflow-auto">
		{#if ingestion.error}
			<p class="text-destructive px-4 py-3 text-sm">{ingestion.error}</p>
		{/if}
		<IngestionTiles />
		<IngestionChart />
		<IngestionSignalsTable />
		<IngestionLog />
	</div>
</div>
