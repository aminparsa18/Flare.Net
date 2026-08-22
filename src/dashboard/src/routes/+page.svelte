<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { LogsExplorerState } from '$lib/logs/state.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import { resolveRequestedSavedView } from '$lib/saved-views/hydrate';
	import { parseLogsDeepLinkParams } from '$lib/deep-links';
	import LogsToolbar from '$lib/components/logs/LogsToolbar.svelte';
	import VolumeChart from '$lib/components/logs/VolumeChart.svelte';
	import ValueDistributionChart from '$lib/components/logs/ValueDistributionChart.svelte';
	import SqlQueryRow from '$lib/components/logs/SqlQueryRow.svelte';
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
			// search, so the branches below are only reached with no (or an invalid) view
			// id. Pattern drill-down ("View occurrences" in PatternsModal) doesn't need a
			// URL round-trip - it calls explorer.applyPatternIdFilter directly, since the
			// modal lives on this same page/state instance. The Metrics "View related
			// logs" deep link (`$lib/deep-links.ts`) does need one, since it's a real
			// cross-route navigation, checked next - same priority position `?view=` sits
			// in, and mutually exclusive with it (a URL is never both at once).
			const view = await resolveRequestedSavedView(page.url, 'Logs');
			const deepLink = view ? null : parseLogsDeepLinkParams(page.url);
			if (view) {
				explorer.applySavedViewState(view.state);
			} else if (deepLink) {
				explorer.applyDeepLinkFilter(deepLink);
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
	<ValueDistributionChart />
	<SqlQueryRow />
	<div class="flex min-h-0 flex-1 flex-col">
		<LogTable />
	</div>
</div>
<EventDetailSheet />
