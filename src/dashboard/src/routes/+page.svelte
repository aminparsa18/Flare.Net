<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { LogsExplorerState } from '$lib/logs/state.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import LogsToolbar from '$lib/components/logs/LogsToolbar.svelte';
	import VolumeChart from '$lib/components/logs/VolumeChart.svelte';
	import LogTable from '$lib/components/logs/LogTable.svelte';
	import EventDetailSheet from '$lib/components/logs/EventDetailSheet.svelte';

	const explorer = logsExplorerContext.set(new LogsExplorerState());

	function handleVisibilityChange() {
		explorer.handleVisibilityChange(document.hidden);
	}

	onMount(() => {
		void explorer.runSearch();
		void explorer.loadKnownServices();
	});

	onDestroy(() => {
		explorer.dispose();
	});
</script>

<svelte:document onvisibilitychange={handleVisibilityChange} />

<svelte:head>
	<title>Flare — Logs</title>
</svelte:head>

<div class="flex h-screen flex-col">
	<LogsToolbar />
	<VolumeChart />
	<div class="flex min-h-0 flex-1 flex-col">
		<LogTable />
	</div>
</div>
<EventDetailSheet />
