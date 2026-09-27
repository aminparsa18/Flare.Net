<script lang="ts">
	// Trace funnels: how many traces pass through an ordered list of span-match steps, where
	// they drop off, and how long each transition takes - see
	// docs-internal/adr/0067-trace-funnels.md. Saved funnels are ordinary saved views with
	// pageType 'Funnels', so ?view=<id> links and last-used restore work the same as on
	// /traces.
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { TraceFunnelState } from '$lib/trace-funnels/state.svelte';
	import { traceFunnelContext } from '$lib/trace-funnels/context';
	import { resolveRequestedSavedView } from '$lib/saved-views/hydrate';
	import { resolveLastUsedSavedView } from '$lib/saved-views/last-used';
	import FunnelToolbar from '$lib/components/trace-funnels/FunnelToolbar.svelte';
	import FunnelStepsEditor from '$lib/components/trace-funnels/FunnelStepsEditor.svelte';
	import FunnelResults from '$lib/components/trace-funnels/FunnelResults.svelte';
	import FunnelTracesSheet from '$lib/components/trace-funnels/FunnelTracesSheet.svelte';
	import * as m from '$lib/paraglide/messages';

	const funnel = traceFunnelContext.set(new TraceFunnelState());

	onMount(() => {
		void (async () => {
			const view =
				(await resolveRequestedSavedView(page.url, 'Funnels')) ??
				(page.url.searchParams.size === 0 ? await resolveLastUsedSavedView('Funnels') : null);
			if (view) funnel.applySavedViewState(view.state);
		})();
	});

	onDestroy(() => {
		funnel.dispose();
	});
</script>

<svelte:head>
	<title>{m.funnelsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<FunnelToolbar />
	<p class="text-muted-foreground px-4 pt-3 text-sm">{m.funnelsPage_intro()}</p>
	<FunnelStepsEditor />
	<FunnelResults />
</div>
<FunnelTracesSheet />
