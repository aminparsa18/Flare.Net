<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { TracesExplorerState } from '$lib/traces/state.svelte';
	import { tracesExplorerContext } from '$lib/traces/context';
	import { ServicesState } from '$lib/services/state.svelte';
	import { servicesContext } from '$lib/services/context';
	import { resolveRequestedSavedView } from '$lib/saved-views/hydrate';
	import { parseTracesDeepLinkParams } from '$lib/deep-links';
	import TracesToolbar from '$lib/components/traces/TracesToolbar.svelte';
	import TraceList from '$lib/components/traces/TraceList.svelte';
	import ServicesToolbar from '$lib/components/services/ServicesToolbar.svelte';
	import ServicesSummaryTiles from '$lib/components/services/ServicesSummaryTiles.svelte';
	import ServicesTable from '$lib/components/services/ServicesTable.svelte';
	import ServiceDependencyGraph from '$lib/components/services/ServiceDependencyGraph.svelte';
	import ServiceCallBreakdownDialog from '$lib/components/services/ServiceCallBreakdownDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	const explorer = tracesExplorerContext.set(new TracesExplorerState());
	const services = servicesContext.set(new ServicesState());

	// Local, page-only UI state - same "doesn't belong on a shared state class" call the
	// trace-detail page's own activeTab already makes. Defaults to 'traces' (the search
	// this page has always opened to); the Services RED rollup is a secondary view
	// reached from here, not the landing experience.
	let activeTab = $state<'traces' | 'services'>('traces');

	// Services' own load()/startPolling() are deliberately deferred to the first time
	// this tab is actually opened, not fired alongside the traces-tab load below - most
	// visits to /traces never touch the Services tab, and there's no reason to run a
	// background ClickHouse aggregate query + a 10s poll loop for a view nobody looked
	// at. Once started it keeps polling for the rest of this page's lifetime (dispose()
	// stops it) even if the user switches back to the traces tab - same "poll while the
	// page is mounted" precedent IngestionState already sets, simpler than pausing/
	// resuming the interval on every tab flip for a saving that's one query per 10s.
	let servicesStarted = false;

	function setActiveTab(tab: 'traces' | 'services'): void {
		if (activeTab === tab) return;
		activeTab = tab;
		if (tab === 'services' && !servicesStarted) {
			servicesStarted = true;
			void services.load();
			services.startPolling();
		}
	}

	onMount(() => {
		void (async () => {
			// ?view=<id> (a saved view's shareable link) takes priority - applySavedViewState
			// already runs the search itself, so the branches below are only reached with
			// no (or an invalid) view id. The deep-link case (?service=&range=) is handled
			// by the $effect below instead of here - see its own comment for why - so this
			// only needs to fall back to a plain default search when neither applies.
			const view = await resolveRequestedSavedView(page.url, 'Traces');
			if (view) {
				explorer.applySavedViewState(view.state);
			} else if (!parseTracesDeepLinkParams(page.url)) {
				void explorer.runSearch();
			}
			void explorer.loadKnownServices();
		})();
	});

	// Re-applies a fresh deep-link arrival (the Metrics "View traces" action, or a
	// service name clicked from the Services tab below) any time the URL carries one -
	// including the very first mount, not only via onMount above. Needed because the
	// Services tab lives on this same /traces route: clicking a service name only
	// changes the query string, so SvelteKit doesn't remount this page and onMount
	// never re-fires - a plain one-shot onMount check (this page's original shape,
	// before Services was folded in here) missed that same-route arrival entirely.
	// parseTracesDeepLinkParams returns null for any URL without both service+range, so
	// this is a no-op for every other navigation (including the user's own filter
	// changes, which never touch the URL - see TracesExplorerState) rather than a
	// second general-purpose bootstrap path fighting with onMount's.
	//
	// lastAppliedDeepLinkKey guards against re-applying (and re-running) the same
	// arrival more than once: applyDeepLinkFilter mutates explorer's own $state, and an
	// effect that both reads reactive state (page.url) and writes reactive state on
	// every run risks Svelte re-scheduling it (effect_update_depth_exceeded, hit live
	// while testing this exact flow) - keying off the URL's raw query string makes each
	// distinct arrival apply exactly once, not once per reactive tick.
	let lastAppliedDeepLinkKey: string | null = null;
	$effect(() => {
		const url = page.url;
		const deepLink = parseTracesDeepLinkParams(url);
		if (deepLink && url.search !== lastAppliedDeepLinkKey) {
			lastAppliedDeepLinkKey = url.search;
			explorer.applyDeepLinkFilter(deepLink);
			activeTab = 'traces';
		}
	});

	onDestroy(() => {
		explorer.dispose();
		services.dispose();
	});
</script>

<svelte:head>
	<title>{m.tracesPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	{#if activeTab === 'traces'}
		<TracesToolbar {activeTab} onTabChange={setActiveTab} />
		<div class="flex min-h-0 flex-1 flex-col">
			<TraceList />
		</div>
	{:else}
		<ServicesToolbar {activeTab} onTabChange={setActiveTab} />
		<!-- Table then map, stacked in one scrollable column - not a Table/Map tab switch
		     (dropped after feedback that a toggle was unnecessary indirection for two views
		     that share one window and are both cheap enough to just show together). The
		     map gets a fixed height (SvelteFlow needs a sized container, unlike the table's
		     natural height) and its own `flex flex-col` wrapper so ServiceDependencyGraph's
		     internal `flex-1`/`h-full` classes still resolve against something, the same as
		     when it had the whole tab's height to itself. -->
		<div class="flex min-h-0 flex-1 flex-col overflow-auto">
			{#if services.error}
				<p class="text-destructive px-4 py-3 text-sm">{services.error}</p>
			{/if}
			<ServicesSummaryTiles />
			<ServicesTable />
			<h3 class="text-muted-foreground px-4 pt-2 text-sm font-medium">{m.servicesPage_dependencyMapHeading()}</h3>
			<div class="flex h-[480px] shrink-0 flex-col px-4 pt-2 pb-4">
				<ServiceDependencyGraph graph={services.graph} />
			</div>
		</div>
	{/if}
</div>
<ServiceCallBreakdownDialog />
