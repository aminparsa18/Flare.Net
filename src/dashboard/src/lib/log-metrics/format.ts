import type { LogFilter } from '$lib/api';
import { SEVERITY_BUCKETS, severityBucketLabel, severityNumbersForBucket } from '$lib/logs/severity';

/** Fields ingest ignores (`PipelineRuleConditionMatcher` evaluates a single log, not a time range or a trace). Dropped from a saved condition. */
const IGNORED_FIELDS = ['from', 'to', 'traceId', 'spanId'] as const;

/** Filter fields the form has no control for; carried through unchanged so "create from this search" keeps them. */
export const EXTRA_FIELDS = ['attributes', 'bodyJsonFilters', 'scopeNames', 'patternId', 'traceSpanFilter', 'searchAllFields'] as const;

/** Names of the carried-over filter fields that are actually set. */
export function extraFieldNames(filter: LogFilter): string[] {
	return EXTRA_FIELDS.filter((key) => {
		const value = filter[key];
		return Array.isArray(value) ? value.length > 0 : !!value;
	});
}

/** `filter` without the fields ingest ignores. */
export function stripIgnored(filter: LogFilter): LogFilter {
	const copy: LogFilter = { ...filter };
	for (const key of IGNORED_FIELDS) delete copy[key];
	return copy;
}

/** A metric-name suggestion from a free-text name: `Checkout errors` -> `logs.checkout_errors`. */
export function suggestMetricName(name: string): string {
	const slug = name
		.trim()
		.toLowerCase()
		.replace(/[^a-z0-9]+/g, '_')
		.replace(/^_+|_+$/g, '');
	return slug ? `logs.${slug}` : '';
}

/** One-line condition summary for the table; `allLogs` is shown when nothing narrows the count. */
export function summarizeCondition(filter: LogFilter, allLogs: string): string {
	const parts: string[] = [];
	if (filter.services?.length) parts.push(filter.services.join(', '));
	const severities = filter.severityNumbers ?? [];
	const labels = SEVERITY_BUCKETS.filter((b) => severityNumbersForBucket(b).every((n) => severities.includes(n))).map((b) =>
		severityBucketLabel(b)
	);
	if (labels.length) parts.push(labels.join('/'));
	if (filter.search) parts.push(`"${filter.search}"`);
	for (const a of filter.attributes ?? []) parts.push(!a.operator || a.operator === 'Equals' ? `${a.key}=${a.value}` : a.key);
	const rest = extraFieldNames({ ...filter, attributes: undefined });
	if (rest.length) parts.push(rest.join(', '));
	return parts.length ? parts.join(' · ') : allLogs;
}
