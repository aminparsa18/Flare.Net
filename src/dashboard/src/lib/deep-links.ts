// Cross-page navigation from Metrics into Logs/Traces, pre-filtered to the service (+ a
// narrowing attribute, for Logs) and time range a chart was showing - "Metrics -> Logs ->
// Traces as one system" rather than three unrelated pages (see MetricChart.svelte's "View
// related logs"/"View traces" actions, the only producer of these links).
//
// Plain query params on the existing routes, not the saved-views `?view=<id>` mechanism -
// a saved view is a *persisted*, named thing (see SavedSearchesMenu); this is a one-off,
// ephemeral hop, same reasoning LogsExplorerState.applyPatternIdFilter's `patternId`
// already establishes for "sticky filter, not a saved view". `+page.svelte` (Logs) and
// `traces/+page.svelte` are the only consumers, in their own onMount, checked after
// `?view=` and before falling back to their normal default.

import type { AttributeFilter } from './api';
import { TIME_RANGE_PRESETS, type TimeRangePreset } from './logs/time-range';

export interface DeepLinkTarget {
	serviceName: string;
	timeRangePreset: TimeRangePreset;
}

export interface LogsDeepLinkTarget extends DeepLinkTarget {
	/** Included only when the caller can name one unambiguous attribute - see MetricChart.svelte's own remarks on when it does. */
	attribute?: { key: string; value: string };
}

function isTimeRangePreset(value: string | null): value is TimeRangePreset {
	// 'custom' excluded deliberately - Metrics never offers it as a selectable preset
	// (see MetricsExplorerState's own remarks), so a deep link never needs to carry one,
	// and Traces has no custom-range support at all to receive it.
	return value != null && TIME_RANGE_PRESETS.some((p) => p.value === value && p.value !== 'custom');
}

export function buildLogsDeepLinkHref(target: LogsDeepLinkTarget): string {
	const params = new URLSearchParams({ service: target.serviceName, range: target.timeRangePreset });
	if (target.attribute) {
		params.set('attrKey', target.attribute.key);
		params.set('attrValue', target.attribute.value);
	}
	return `/?${params.toString()}`;
}

export function buildTracesDeepLinkHref(target: DeepLinkTarget): string {
	const params = new URLSearchParams({ service: target.serviceName, range: target.timeRangePreset });
	return `/traces?${params.toString()}`;
}

export interface ParsedLogsDeepLink {
	services: string[];
	timeRangePreset: TimeRangePreset;
	attribute: AttributeFilter | null;
}

/** Parses `+page.svelte`'s (root, Logs) deep-link params - null when this isn't a deep-link arrival (a direct visit, bookmark, or `?view=` saved view - checked first by the caller). */
export function parseLogsDeepLinkParams(url: URL): ParsedLogsDeepLink | null {
	const service = url.searchParams.get('service');
	const range = url.searchParams.get('range');
	if (!service || !isTimeRangePreset(range)) return null;
	const attrKey = url.searchParams.get('attrKey');
	const attrValue = url.searchParams.get('attrValue');
	return {
		services: [service],
		timeRangePreset: range,
		// 'Log' bag: a metric's DataPointAttributes is a per-data-point attribute, the
		// same shape as a log record's own LogAttributes - not resource-level, which is
		// where service.name/deployment.environment usually live instead (see
		// MetricSeriesQueryBuilder's remarks on why ServiceName is its own column,
		// separate from DataPointAttributes). Best-effort, not a guarantee: a metric
		// attribute with no matching log attribute just finds nothing, same as any other
		// over-narrow filter.
		attribute: attrKey && attrValue ? { bag: 'Log', key: attrKey, value: attrValue } : null
	};
}

export interface ParsedTracesDeepLink {
	services: string[];
	timeRangePreset: TimeRangePreset;
}

/** Parses `traces/+page.svelte`'s deep-link params - null when this isn't a deep-link arrival. */
export function parseTracesDeepLinkParams(url: URL): ParsedTracesDeepLink | null {
	const service = url.searchParams.get('service');
	const range = url.searchParams.get('range');
	if (!service || !isTimeRangePreset(range)) return null;
	return { services: [service], timeRangePreset: range };
}