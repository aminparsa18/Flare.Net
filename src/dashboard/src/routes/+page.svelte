<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { goto } from '$app/navigation';
	import { LogsExplorerState } from '$lib/logs/state.svelte';
	import { logsExplorerContext } from '$lib/logs/context';
	import { resolveRequestedSavedView } from '$lib/saved-views/hydrate';
	import { resolveLastUsedSavedView } from '$lib/saved-views/last-used';
	import { parseLogsDeepLinkParams, parseLogContextDeepLinkParams, parseLogsStateDeepLinkParam } from '$lib/deep-links';
	import { getHomeDashboardId } from '$lib/dashboards/home-preference';
	import { dashboardPath } from '$lib/dashboards/page-paths';
	import LogsToolbar from '$lib/components/logs/LogsToolbar.svelte';
	import VolumeChart from '$lib/components/logs/VolumeChart.svelte';
	import ValueDistributionChart from '$lib/components/logs/ValueDistributionChart.svelte';
	import AttributeFiltersRow from '$lib/components/logs/AttributeFiltersRow.svelte';
	import BodyJsonFiltersRow from '$lib/components/logs/BodyJsonFiltersRow.svelte';
	import SqlQueryRow from '$lib/components/logs/SqlQueryRow.svelte';
	import LogTable from '$lib/components/logs/LogTable.svelte';
	import EventDetailSheet from '$lib/components/logs/EventDetailSheet.svelte';
	import LogContextSheet from '$lib/components/logs/LogContextSheet.svelte';
	import FacetSidebar from '$lib/components/facets/FacetSidebar.svelte';
	import { FacetSidebarPrefs } from '$lib/facets/prefs.svelte';
	import { DEFAULT_LOG_ATTRIBUTE_FACETS, LOG_FACET_BAGS, logFacetDefinitions, logFacetReloadKey } from '$lib/logs/facets';
	import * as m from '$lib/paraglide/messages';

	const explorer = logsExplorerContext.set(new LogsExplorerState());

	const facetPrefs = new FacetSidebarPrefs('flare.logs.facetSidebar', DEFAULT_LOG_ATTRIBUTE_FACETS, LOG_FACET_BAGS);
	const facets = $derived(logFacetDefinitions(explorer, facetPrefs));
	const facetReloadKey = $derived(logFacetReloadKey(explorer));
	const facetBagOptions = [
		{ value: 'Log' as const, label: m.attributeFilters_bagLog() },
		{ value: 'Resource' as const, label: m.attributeFilters_bagResource() },
		{ value: 'Scope' as const, label: m.attributeFilters_bagScope() }
	];

	function handleVisibilityChange() {
		explorer.handleVisibilityChange(document.hidden);
	}

	onMount(() => {
		// A "set as home" dashboard (Phase 3, $lib/dashboards/home-preference.ts) takes over
		// this bare "/" only when nothing else is asking to land here - a `?view=<id>`
		// shareable link or a Metrics "View related logs" deep link (both checked below via
		// searchParams.size) still means "show the Logs Explorer", not the home dashboard.
		// replaceState so the redirect doesn't leave an extra "/" entry for Back to land on.
		if (page.url.searchParams.size === 0) {
			const homeId = getHomeDashboardId();
			if (homeId) {
				void goto(dashboardPath({ id: homeId }), { replaceState: true });
				return;
			}
		}

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
			// A fired alert's `?state=` link (`$lib/deep-links.ts`) carries a whole saved-view
			// state inline, so it restores through the same applySavedViewState path.
			const view = await resolveRequestedSavedView(page.url, 'Logs');
			const inlineState = view ? null : parseLogsStateDeepLinkParam(page.url);
			const deepLink = view || inlineState ? null : parseLogsDeepLinkParams(page.url);
			// A bare visit (no params at all, and no home dashboard - that returned above)
			// restores the saved search last picked here ($lib/saved-views/last-used.ts),
			// through the same applySavedViewState path as `?view=`.
			const lastUsed = page.url.searchParams.size === 0 ? await resolveLastUsedSavedView('Logs') : null;
			if (view) {
				explorer.applySavedViewState(view.state);
			} else if (lastUsed) {
				explorer.applySavedViewState(lastUsed.state);
			} else if (inlineState) {
				explorer.applySavedViewState(inlineState);
			} else if (deepLink) {
				explorer.applyDeepLinkFilter(deepLink);
			} else if (explorer.live) {
				explorer.startLiveTail();
			} else {
				void explorer.runSearch();
			}
			void explorer.loadKnownServices();

			// A `?context=`/`?ts=` permalink (LogContextSheet's "Copy link") opens the
			// context sheet *on top of* whatever the branches above just loaded, rather
			// than replacing them - unlike `?view=`/the Metrics deep link, it isn't a
			// request to change the underlying search at all, see its own remarks.
			const contextDeepLink = parseLogContextDeepLinkParams(page.url);
			if (contextDeepLink) {
				explorer.openContextFromDeepLink(contextDeepLink);
			}
		})();
	});

	onDestroy(() => {
		explorer.dispose();
	});
</script>

<svelte:document onvisibilitychange={handleVisibilityChange} />

<svelte:head>
	<title>{m.logsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<LogsToolbar />
	<div class="flex min-h-0 flex-1">
		<FacetSidebar {facets} reloadKey={facetReloadKey} prefs={facetPrefs} bagOptions={facetBagOptions} />
		<div class="flex min-w-0 flex-1 flex-col">
			<VolumeChart />
			<ValueDistributionChart />
			<AttributeFiltersRow />
			<BodyJsonFiltersRow />
			<SqlQueryRow />
			<div class="flex min-h-0 flex-1 flex-col">
				<LogTable />
			</div>
		</div>
	</div>
</div>
<EventDetailSheet />
<LogContextSheet />
