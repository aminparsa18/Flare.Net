// The Logs page's facet sidebar sections (FacetSidebar.svelte): Service and Severity
// from built-in columns, then one section per chosen attribute (environment and host by
// default). Every count comes from /api/logs/attribute-values under the page's current
// filter with that facet's own selection left out - see FacetDefinition.load.

import { getLogAttributeValues, type AttributeBag, type LogFilter } from '$lib/api';
import type { FacetDefinition, FacetOption } from '$lib/facets/types';
import type { FacetSidebarPrefs } from '$lib/facets/prefs.svelte';
import { selectedAttributeValues, withAttributeSelection, withoutAttributeSelection } from '$lib/facets/attribute-selection';
import { SEVERITY_BUCKETS, severityBucketFor, severityBucketLabel, severityNumbersForBucket, type SeverityBucket } from './severity';
import type { LogsExplorerState } from './state.svelte';
import * as m from '$lib/paraglide/messages';

/** Distinct values fetched per section - FacetSection shows the first few, the rest behind "Show more". */
const FACET_LIMIT = 50;

export const LOG_FACET_BAGS = ['Log', 'Resource', 'Scope'] as const satisfies readonly AttributeBag[];

/** Shown until the user removes them - `deployment.environment` is the key this codebase's examples and docs use. */
export const DEFAULT_LOG_ATTRIBUTE_FACETS: { bag: AttributeBag; key: string }[] = [
	{ bag: 'Resource', key: 'deployment.environment' },
	{ bag: 'Resource', key: 'host.name' }
];

function bucketById(id: string): SeverityBucket | undefined {
	return SEVERITY_BUCKETS.find((b) => b.id === id) ?? (id === 'unspecified' ? severityBucketFor(0) : undefined);
}

/**
 * Changes whenever anything the facet counts depend on does - the content filter and
 * the time window (preset, custom range, or a VolumeChart bar selection). Display-only
 * fields (lines per row, volume group-by, ...) are deliberately not part of it.
 */
export function logFacetReloadKey(explorer: LogsExplorerState): string {
	return JSON.stringify([
		explorer.buildFilter(null),
		explorer.filter.timeRangePreset,
		explorer.filter.customRange,
		explorer.selectedBucketRange
	]);
}

export function logFacetDefinitions(explorer: LogsExplorerState, prefs: FacetSidebarPrefs<AttributeBag>): FacetDefinition[] {
	function filterWithout(strip: (filter: LogFilter) => LogFilter): LogFilter {
		return strip(explorer.buildFilter(explorer.currentRange()));
	}

	async function values(filter: LogFilter, request: { field: 'Service' | 'Severity' } | { bag: AttributeBag; key: string }, signal: AbortSignal) {
		const res = await getLogAttributeValues(
			'field' in request ? { filter, key: '', field: request.field, limit: FACET_LIMIT } : { filter, bag: request.bag, key: request.key, limit: FACET_LIMIT },
			signal
		);
		return res.values;
	}

	const selectedSeverityIds = [...SEVERITY_BUCKETS, severityBucketFor(0)]
		.filter((b) => {
			const numbers = severityNumbersForBucket(b);
			return numbers.every((n) => explorer.filter.severityNumbers.includes(n));
		})
		.map((b) => b.id);

	const builtIn: FacetDefinition[] = [
		{
			id: 'service',
			title: m.facets_service(),
			selected: explorer.filter.services,
			onChange: (next) => explorer.setServices(next),
			load: (signal) => values(filterWithout(({ services: _, ...rest }) => rest), { field: 'Service' }, signal)
		},
		{
			id: 'severity',
			title: m.facets_severity(),
			selected: selectedSeverityIds,
			label: (id) => {
				const bucket = bucketById(id);
				return bucket ? severityBucketLabel(bucket) : id;
			},
			onChange: (next) =>
				explorer.setSeverityNumbers(
					next
						.map(bucketById)
						.filter((b) => b != null)
						.flatMap(severityNumbersForBucket)
				),
			// Server returns one row per SeverityNumber (0-24); rolled up here into the same
			// six buckets the toolbar's severity filter uses, in severity order.
			load: async (signal) => {
				const rows = await values(filterWithout(({ severityNumbers: _, ...rest }) => rest), { field: 'Severity' }, signal);
				const counts = new Map<string, number>();
				for (const row of rows) {
					const id = severityBucketFor(Number(row.value)).id;
					counts.set(id, (counts.get(id) ?? 0) + row.count);
				}
				const order = [...SEVERITY_BUCKETS.map((b) => b.id), 'unspecified'];
				return order.filter((id) => counts.has(id)).map((id): FacetOption => ({ value: id, count: counts.get(id)! }));
			}
		}
	];

	const attributes = prefs.attributes.map(({ bag, key }): FacetDefinition => ({
		id: `attr:${bag}:${key}`,
		title: key,
		selected: selectedAttributeValues(explorer.filter.attributeFilters, bag, key),
		onChange: (next) => explorer.setAttributeFilters(withAttributeSelection(explorer.filter.attributeFilters, bag, key, next)),
		onRemove: () => prefs.removeAttribute(bag, key),
		load: (signal) =>
			values(
				explorer.buildFilter(explorer.currentRange(), withoutAttributeSelection(explorer.filter.attributeFilters, bag, key)),
				{ bag, key },
				signal
			)
	}));

	return [...builtIn, ...attributes];
}
