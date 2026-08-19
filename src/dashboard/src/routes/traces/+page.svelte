<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { TracesExplorerState } from '$lib/traces/state.svelte';
	import { tracesExplorerContext } from '$lib/traces/context';
	import { resolveRequestedSavedView } from '$lib/saved-views/hydrate';
	import { parseTracesDeepLinkParams } from '$lib/deep-links';
	import TracesToolbar from '$lib/components/traces/TracesToolbar.svelte';
	import TraceList from '$lib/components/traces/TraceList.svelte';

	const explorer = tracesExplorerContext.set(new TracesExplorerState());

	onMount(() => {
		void (async () => {
			// ?view=<id> (a saved view's shareable link) takes priority - applySavedViewState
			// already runs the search itself, so the branches below are only reached with
			// no (or an invalid) view id. The Metrics "View traces" deep link
			// (`$lib/deep-links.ts`) is checked next, same priority position/mutual-
			// exclusivity with `?view=` as the Logs page's own onMount.
			const view = await resolveRequestedSavedView(page.url, 'Traces');
			const deepLink = view ? null : parseTracesDeepLinkParams(page.url);
			if (view) {
				explorer.applySavedViewState(view.state);
			} else if (deepLink) {
				explorer.applyDeepLinkFilter(deepLink);
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

<svelte:head>
	<title>Flare — Traces</title>
</svelte:head>

<div class="flex h-full flex-col">
	<TracesToolbar />
	<div class="flex min-h-0 flex-1 flex-col">
		<TraceList />
	</div>
</div>
