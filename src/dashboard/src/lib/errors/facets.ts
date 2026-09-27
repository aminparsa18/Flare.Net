// The /errors page's facet sidebar sections (FacetSidebar.svelte): Service, then one
// section per chosen resource attribute. Counts are exception events (what the groups
// table's Occurrences column counts), from `POST /api/errors/facet-values`. See
// `$lib/traces/facets.ts` for the Traces counterpart.
//
// Resource-attribute sections are single-select: ExceptionFilter.ResourceAttributes is
// equality-only (see ResourceAttributeFilter.cs), so one key can hold one value. Picking a
// value sets that key's filter chip in the resource-attribute row; picking another
// replaces it.

import { getExceptionFacetValues, type ExceptionFilter } from '$lib/errors-api';
import type { FacetDefinition } from '$lib/facets/types';
import type { FacetSidebarPrefs } from '$lib/facets/prefs.svelte';
import type { ResourceAttributeFilter } from '$lib/services-api';
import type { ErrorsExplorerState } from './state.svelte';
import * as m from '$lib/paraglide/messages';

const FACET_LIMIT = 50;

/** The only bag here - exceptions are filtered by their span's resource, nothing else. */
export const ERROR_FACET_BAGS = ['Resource'] as const;
export type ErrorFacetBag = (typeof ERROR_FACET_BAGS)[number];

/** Shown until the user removes them - the two scopes the feature was asked for ("production only", "only version 2.3"). */
export const DEFAULT_ERROR_ATTRIBUTE_FACETS: { bag: ErrorFacetBag; key: string }[] = [
	{ bag: 'Resource', key: 'deployment.environment' },
	{ bag: 'Resource', key: 'service.version' }
];

/** Changes whenever the fetched scope does - the filter sent to the API plus the time-range preset. */
export function errorFacetReloadKey(errors: ErrorsExplorerState): string {
	return JSON.stringify([errors.buildFilter(null), errors.filter.timeRangePreset, errors.filter.customRange]);
}

export function errorFacetDefinitions(errors: ErrorsExplorerState, prefs: FacetSidebarPrefs<ErrorFacetBag>): FacetDefinition[] {
	function scopedFilter(strip: (filter: ExceptionFilter) => ExceptionFilter): ExceptionFilter {
		return strip(errors.buildFilter(errors.currentRange()));
	}

	const service: FacetDefinition = {
		id: 'service',
		title: m.facets_service(),
		selected: errors.filter.services,
		onChange: (next) => errors.setServices(next),
		load: async (signal) => {
			const filter = scopedFilter(({ services: _, ...rest }) => rest);
			return (await getExceptionFacetValues({ filter, field: 'Service', limit: FACET_LIMIT }, signal)).values;
		}
	};

	const attributes = prefs.attributes.map(({ bag, key }): FacetDefinition => {
		const others = (filters: ResourceAttributeFilter[]) => filters.filter((f) => f.key !== key);
		const current = errors.filter.resourceAttributes.find((f) => f.key === key);
		return {
			id: `attr:${bag}:${key}`,
			title: key,
			single: true,
			selected: current ? [current.value] : [],
			onChange: (next) => {
				const rest = others(errors.filter.resourceAttributes);
				errors.setResourceAttributes(next.length ? [...rest, { key, value: next[0] }] : rest);
			},
			onRemove: () => prefs.removeAttribute(bag, key),
			load: async (signal) => {
				const filter = scopedFilter(({ resourceAttributes, ...rest }) => {
					const remaining = others(resourceAttributes ?? []);
					return remaining.length ? { ...rest, resourceAttributes: remaining } : rest;
				});
				return (await getExceptionFacetValues({ filter, field: 'ResourceAttribute', key, limit: FACET_LIMIT }, signal)).values;
			}
		};
	});

	return [service, ...attributes];
}
