// The Traces page's facet sidebar sections (FacetSidebar.svelte): Service, Status, Kind,
// Operation and Duration from built-in span columns, then one section per chosen span
// attribute. Counts are traces (root spans), matching what TraceList shows - every
// filter on this page applies to the root span row, so the facets do too. See
// `$lib/logs/facets.ts` for the Logs counterpart.

import { getSpanAttributeValues, type SpanAttributeBag, type SpanFilter, type SpanValuesField } from '$lib/traces-api';
import type { FacetDefinition, FacetOption } from '$lib/facets/types';
import type { FacetSidebarPrefs } from '$lib/facets/prefs.svelte';
import { selectedAttributeValues, withAttributeSelection, withoutAttributeSelection } from '$lib/facets/attribute-selection';
import { DURATION_BUCKET_LOWER_BOUNDS_NANO, durationBucketLabel } from './duration-buckets';
import { kindLabel, statusLabel } from './status';
import type { TracesExplorerState } from './state.svelte';
import * as m from '$lib/paraglide/messages';

const FACET_LIMIT = 50;

export const TRACE_FACET_BAGS = ['Span', 'Resource', 'Scope'] as const satisfies readonly SpanAttributeBag[];

/** See `logFacetReloadKey` - the content filter plus the time-range preset. */
export function traceFacetReloadKey(explorer: TracesExplorerState): string {
	return JSON.stringify([explorer.buildFilter(null), explorer.filter.timeRangePreset]);
}

export function traceFacetDefinitions(explorer: TracesExplorerState, prefs: FacetSidebarPrefs<SpanAttributeBag>): FacetDefinition[] {
	function builtIn(field: SpanValuesField, strip: (filter: SpanFilter) => SpanFilter) {
		return async (signal: AbortSignal): Promise<FacetOption[]> => {
			const filter = strip(explorer.buildFilter(explorer.currentRange()));
			const res = await getSpanAttributeValues({ filter, key: '', field, limit: FACET_LIMIT }, signal);
			return res.values;
		};
	}

	const durationLoad = builtIn('DurationBucket', ({ minDurationNano: _min, maxDurationNano: _max, ...rest }) => rest);

	const facets: FacetDefinition[] = [
		{
			id: 'service',
			title: m.facets_service(),
			selected: explorer.filter.services,
			onChange: (next) => explorer.setServices(next),
			load: builtIn('Service', ({ services: _, ...rest }) => rest)
		},
		{
			id: 'status',
			title: m.facets_status(),
			selected: explorer.filter.statusCodes,
			label: statusLabel,
			onChange: (next) => explorer.setStatusCodes(next),
			load: builtIn('Status', ({ statusCodes: _, ...rest }) => rest)
		},
		{
			id: 'kind',
			title: m.facets_kind(),
			selected: explorer.filter.kinds.map(String),
			label: (value) => kindLabel(Number(value)),
			onChange: (next) => explorer.setKinds(next.map(Number)),
			load: builtIn('Kind', ({ kinds: _, ...rest }) => rest)
		},
		{
			id: 'operation',
			title: m.facets_operation(),
			selected: explorer.filter.names,
			onChange: (next) => explorer.setNames(next),
			load: builtIn('Name', ({ names: _, ...rest }) => rest)
		},
		{
			id: 'duration',
			title: m.facets_duration(),
			single: true,
			selected: explorer.filter.durationBucketNano === null ? [] : [String(explorer.filter.durationBucketNano)],
			label: (value) => durationBucketLabel(Number(value)),
			onChange: (next) => explorer.setDurationBucketNano(next.length ? Number(next[0]) : null),
			// Server orders by count; buckets read better shortest-first.
			load: async (signal) => {
				const rows = await durationLoad(signal);
				const order: readonly number[] = DURATION_BUCKET_LOWER_BOUNDS_NANO;
				return rows.toSorted((a, b) => order.indexOf(Number(a.value)) - order.indexOf(Number(b.value)));
			}
		}
	];

	const attributes = prefs.attributes.map(({ bag, key }): FacetDefinition => ({
		id: `attr:${bag}:${key}`,
		title: key,
		selected: selectedAttributeValues(explorer.filter.attributeFilters, bag, key),
		onChange: (next) => explorer.setAttributeFilters(withAttributeSelection(explorer.filter.attributeFilters, bag, key, next)),
		onRemove: () => prefs.removeAttribute(bag, key),
		load: async (signal) => {
			const filter = explorer.buildFilter(explorer.currentRange(), {
				attributeFilters: withoutAttributeSelection(explorer.filter.attributeFilters, bag, key)
			});
			const res = await getSpanAttributeValues({ filter, bag, key, limit: FACET_LIMIT }, signal);
			return res.values;
		}
	}));

	return [...facets, ...attributes];
}
