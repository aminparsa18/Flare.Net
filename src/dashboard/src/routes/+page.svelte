<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { goto, replaceState } from '$app/navigation';
	import { LogsExplorerState } from '#lib/logs/state.svelte.ts';
	import { logsExplorerContext } from '#lib/logs/context.ts';
	import { resolveRequestedSavedView } from '#lib/saved-views/hydrate.ts';
	import { resolveLastUsedSavedView } from '#lib/saved-views/last-used.ts';
	import { parseLogsDeepLinkParams, parseLogContextDeepLinkParams, parseStateDeepLinkParam } from '#lib/deep-links.ts';
	import { explorerPrefs } from '#lib/explorer/prefs.svelte.ts';
	import { withBase } from '#lib/paths.ts';
	import { getHomeDashboardId } from '#lib/dashboards/home-preference.ts';
	import { dashboardPath } from '#lib/dashboards/page-paths.ts';
	import NaturalLanguageFilter from '#lib/components/NaturalLanguageFilter.svelte';
	import LogsToolbar from '#lib/components/logs/LogsToolbar.svelte';
	import VolumeChart from '#lib/components/logs/VolumeChart.svelte';
	import ValueDistributionChart from '#lib/components/logs/ValueDistributionChart.svelte';
	import AttributeFiltersRow from '#lib/components/logs/AttributeFiltersRow.svelte';
	import BodyJsonFiltersRow from '#lib/components/logs/BodyJsonFiltersRow.svelte';
	import SqlQueryRow from '#lib/components/logs/SqlQueryRow.svelte';
	import LogTable from '#lib/components/logs/LogTable.svelte';
	import EventDetailSheet from '#lib/components/logs/EventDetailSheet.svelte';
	import LogContextSheet from '#lib/components/logs/LogContextSheet.svelte';
	import FacetSidebar from '#lib/components/facets/FacetSidebar.svelte';
	import { FacetSidebarPrefs } from '#lib/facets/prefs.svelte.ts';
	import { DEFAULT_LOG_ATTRIBUTE_FACETS, LOG_FACET_BAGS, logFacetDefinitions, logFacetReloadKey } from '#lib/logs/facets.ts';
	import { tourController } from '#lib/tour/tour.svelte.ts';
	import { logsTour } from '#lib/tour/tours/logs.ts';
	import * as m from '#lib/paraglide/messages.js';

	const explorer = logsExplorerContext.set(new LogsExplorerState());

	const facetPrefs = new FacetSidebarPrefs('flare.logs.facetSidebar', DEFAULT_LOG_ATTRIBUTE_FACETS, LOG_FACET_BAGS);
	const facets = $derived(logFacetDefinitions(explorer, facetPrefs));
	const facetReloadKey = $derived(logFacetReloadKey(explorer));
	const facetBagOptions = [
		{ value: 'Log' as const, label: m.attributeFilters_bagLog() },
		{ value: 'Resource' as const, label: m.attributeFilters_bagResource() },
		{ value: 'Scope' as const, label: m.attributeFilters_bagScope() }
	];

	let tourTimer: ReturnType<typeof setTimeout> | undefined;

	function handleVisibilityChange() {
		explorer.handleVisibilityChange(document.hidden);
	}

	onMount(() => {
		// A "set as home" dashboard (Phase 3, #lib/dashboards/home-preference.ts) takes over
		// this bare "/" only when nothing else is asking to land here - a `?view=<id>`
		// shareable link or a Metrics "View related logs" deep link (both checked below via
		// searchParams.size) still means "show the Logs Explorer", not the home dashboard.
		// replaceState so the redirect doesn't leave an extra "/" entry for Back to land on.
		if (page.url.searchParams.size === 0) {
			// Settings > Explorer's landing page wins over the home dashboard when it names another page.
			if (explorerPrefs.landingPage !== 'default' && explorerPrefs.landingPage !== 'logs') {
				void goto(withBase(`/${explorerPrefs.landingPage}`), { replace: true });
				return;
			}
			const homeId = explorerPrefs.landingPage === 'logs' ? null : getHomeDashboardId();
			if (homeId) {
				void goto(dashboardPath({ id: homeId }), { replace: true });
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
			// logs" deep link (`#lib/deep-links.ts`) does need one, since it's a real
			// cross-route navigation, checked next - same priority position `?view=` sits
			// in, and mutually exclusive with it (a URL is never both at once).
			// A fired alert's `?state=` link (`#lib/deep-links.ts`) carries a whole saved-view
			// state inline, so it restores through the same applySavedViewState path.
			const view = await resolveRequestedSavedView(page.url, 'Logs');
			const inlineState = view ? null : parseStateDeepLinkParam(page.url);
			const deepLink = view || inlineState ? null : parseLogsDeepLinkParams(page.url);
			// A bare visit (no params at all, and no home dashboard - that returned above)
			// restores the saved search last picked here (#lib/saved-views/last-used.ts),
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

			// `?tour=logs` (Settings > Explorer > Guided tours' Replay) always starts it, then drops
			// the param so a reload doesn't replay it again. Otherwise first visit only
			// (#lib/tour/), and only on a bare visit - someone arriving from a shared link or alert
			// came to look at something specific. The delay lets the toolbar and first results
			// render so the spotlight lands on settled layout.
			if (page.url.searchParams.get('tour') === logsTour.id) {
				const url = new URL(page.url.href);
				url.searchParams.delete('tour');
				replaceState(url, page.state);
				tourTimer = setTimeout(() => tourController.start(logsTour), 800);
			} else if (page.url.searchParams.size === 0) {
				tourTimer = setTimeout(() => tourController.maybeAutoStart(logsTour), 800);
			}

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
		clearTimeout(tourTimer);
		tourController.cancel(logsTour);
		explorer.dispose();
	});
</script>

<svelte:document onvisibilitychange={handleVisibilityChange} />

<svelte:head>
	<title>{m.logsPage_title()}</title>
</svelte:head>

<!-- The `data-tour` wrappers are the Logs tour's anchors (#lib/tour/tours/logs.ts); `contents`
     keeps them out of the layout. -->
<div class="flex h-full flex-col">
	<LogsToolbar />
	<div class="contents" data-tour="logs-ai">
		<NaturalLanguageFilter
			target="Logs"
			knownServices={() => explorer.knownServices}
			apply={(f) =>
				explorer.applySavedViewState({
					timeRangePreset: f.timeRangePreset,
					customRange: f.customRange ?? null,
					services: f.services,
					severityNumbers: f.severityNumbers,
					search: f.search,
					attributeFilters: f.attributeFilters
				})}
		/>
	</div>
	<div class="flex min-h-0 flex-1">
		<div class="contents" data-tour="logs-facets"><FacetSidebar {facets} reloadKey={facetReloadKey} prefs={facetPrefs} bagOptions={facetBagOptions} /></div>
		<div class="flex min-w-0 flex-1 flex-col">
			<div class="contents" data-tour="logs-volume"><VolumeChart releaseMarkers /></div>
			<div class="contents" data-tour="logs-value-distribution"><ValueDistributionChart /></div>
			<div class="contents" data-tour="logs-attribute-filters"><AttributeFiltersRow /></div>
			<div class="contents" data-tour="logs-json-filters"><BodyJsonFiltersRow /></div>
			<div class="contents" data-tour="logs-sql"><SqlQueryRow /></div>
			<div class="flex min-h-0 flex-1 flex-col" data-tour="logs-table">
				<LogTable />
			</div>
		</div>
	</div>
</div>
<EventDetailSheet />
<LogContextSheet />
