<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { page } from '$app/state';
	import { parseErrorsStateDeepLinkParam } from '$lib/deep-links';
	import { ErrorsExplorerState } from '$lib/errors/state.svelte';
	import { errorsExplorerContext } from '$lib/errors/context';
	import ErrorsToolbar from '$lib/components/errors/ErrorsToolbar.svelte';
	import ResourceAttributeFiltersRow from '$lib/components/services/ResourceAttributeFiltersRow.svelte';
	import ExceptionGroupsTable from '$lib/components/errors/ExceptionGroupsTable.svelte';
	import ExceptionOccurrenceDialog from '$lib/components/errors/ExceptionOccurrenceDialog.svelte';
	import FacetSidebar from '$lib/components/facets/FacetSidebar.svelte';
	import { FacetSidebarPrefs } from '$lib/facets/prefs.svelte';
	import { DEFAULT_ERROR_ATTRIBUTE_FACETS, ERROR_FACET_BAGS, errorFacetDefinitions, errorFacetReloadKey } from '$lib/errors/facets';
	import * as m from '$lib/paraglide/messages';

	const errors = errorsExplorerContext.set(new ErrorsExplorerState());

	const facetPrefs = new FacetSidebarPrefs('flare.errors.facetSidebar', DEFAULT_ERROR_ATTRIBUTE_FACETS, ERROR_FACET_BAGS);
	const facets = $derived(errorFacetDefinitions(errors, facetPrefs));
	const facetReloadKey = $derived(errorFacetReloadKey(errors));
	const facetBagOptions = [{ value: 'Resource' as const, label: m.attributeFilters_bagResource() }];

	onMount(() => {
		// A fired exception alert's `?state=` link ($lib/deep-links.ts) scopes the page to
		// the rule's type, services and evaluated window instead of the default last hour.
		const deepLink = parseErrorsStateDeepLinkParam(page.url);
		void (deepLink ? errors.applyDeepLinkState(deepLink) : errors.runSearch());
	});

	onDestroy(() => {
		errors.dispose();
	});
</script>

<svelte:head>
	<title>{m.errorsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<ErrorsToolbar />
	<div class="flex min-h-0 flex-1">
		<FacetSidebar {facets} reloadKey={facetReloadKey} prefs={facetPrefs} bagOptions={facetBagOptions} />
		<div class="flex min-w-0 flex-1 flex-col">
			<ResourceAttributeFiltersRow
				filters={errors.filter.resourceAttributes}
				onChange={(filters) => errors.setResourceAttributes(filters)}
				collapseStorageKey="flare.errors.resourceAttributeFiltersCollapsed"
			/>
			<div class="min-h-0 flex-1 overflow-auto">
				<ExceptionGroupsTable />
			</div>
		</div>
	</div>
</div>
<ExceptionOccurrenceDialog />
