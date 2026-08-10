<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { LogsExplorerState } from '$lib/logs/state.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import { resolveRequestedSavedView } from '$lib/saved-views/hydrate';
	import LogsToolbar from '$lib/components/logs/LogsToolbar.svelte';
	import VolumeChart from '$lib/components/logs/VolumeChart.svelte';
	import LogTable from '$lib/components/logs/LogTable.svelte';
	import EventDetailSheet from '$lib/components/logs/EventDetailSheet.svelte';

	const explorer = logsExplorerContext.set(new LogsExplorerState());

	function handleVisibilityChange() {
		explorer.handleVisibilityChange(document.hidden);
	}

	onMount(() => {
		void (async () => {
			// ?view=<id> (a saved view's shareable link) takes priority over the live-by-
			// default startup - applySavedViewState turns live off itself and runs the
			// search, so the branch below is only reached with no (or an invalid) view id.
			const view = await resolveRequestedSavedView(page.url, 'Logs');
			if (view) {
				explorer.applySavedViewState(view.state);
			} else if (explorer.live) {
				explorer.startLiveTail();
			} else {
				void explorer.runSearch();
			}
			void explorer.loadKnownServices();
		})();
	});

	onDestroy(() => {
		explorer.dispose();
	});
</script>

<svelte:document onvisibilitychange={handleVisibilityChange} />

<svelte:head>
	<title>Flare — Logs</title>
</svelte:head>

<div class="flex h-full flex-col">
	<LogsToolbar />
	<VolumeChart />
	<div class="flex min-h-0 flex-1 flex-col">
		<LogTable />
	</div>
</div>
<EventDetailSheet />
