<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { IngestionState } from '$lib/ingestion/state.svelte';
	import { ingestionContext } from '$lib/ingestion/context';
	import IngestionToolbar from '$lib/components/ingestion/IngestionToolbar.svelte';
	import IngestionTiles from '$lib/components/ingestion/IngestionTiles.svelte';
	import IngestionChart from '$lib/components/ingestion/IngestionChart.svelte';
	import IngestionReceivers from '$lib/components/ingestion/IngestionReceivers.svelte';
	import IngestionSignalsTable from '$lib/components/ingestion/IngestionSignalsTable.svelte';
	import IngestionLog from '$lib/components/ingestion/IngestionLog.svelte';
	import PipelineStreamsTable from '$lib/components/ingestion/PipelineStreamsTable.svelte';
	import PipelineFlushHealthTable from '$lib/components/ingestion/PipelineFlushHealthTable.svelte';
	import PipelineServiceBreakdown from '$lib/components/ingestion/PipelineServiceBreakdown.svelte';
	import IngestionTopology from '$lib/components/ingestion/IngestionTopology.svelte';

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
		<IngestionReceivers />
		<IngestionSignalsTable />
		<IngestionLog />

		<!-- Pipeline health (Planning.md v10) - "is the buffered pipeline keeping up",
		     below the v8 "is data arriving" section rather than a separate page/nav
		     entry, per this item's own title. -->
		<div class="border-t pt-4">
			<h1 class="mb-1 px-4 text-sm font-semibold">Pipeline health</h1>
			<PipelineStreamsTable />
			<PipelineFlushHealthTable />
			<PipelineServiceBreakdown />
		</div>

		<!-- Synthesizes the two sections above into one picture (Planning.md v10 follow-up)
		     - deliberately last, as a capstone over data the reader has already seen
		     piecemeal, not a first-thing "overview" that would duplicate the health
		     indicator already at the top of the page. -->
		<IngestionTopology />
	</div>
</div>
