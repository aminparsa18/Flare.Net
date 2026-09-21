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
import type { MetricPointType } from './metrics-api';

export interface DeepLinkTarget {
	serviceName: string;
	timeRangePreset: TimeRangePreset;
}

export interface LogsDeepLinkTarget extends DeepLinkTarget {
	/** Included only when the caller can name one unambiguous attribute - see MetricChart.svelte's own remarks on when it does. */
	attribute?: { key: string; value: string };
}

function isTimeRangePreset(value: string | null): value is TimeRangePreset {
	// 'custom' excluded deliberately - Metrics' toolbar still never offers it as a
	// selectable preset (see MetricsExplorerState's own remarks), but MetricChart's
	// drag-to-zoom *can* land the filter on 'custom' with a concrete range now. These
	// params carry only a preset value, with nothing to round-trip an explicit from/to
	// through, so a deep link built while zoomed just gets no `range` param at all (see
	// MetricChart's own logsHref/tracesHref remarks on why that's the honest choice) -
	// this parser side stays as strict as before, and Traces still has no custom-range
	// support at all to receive one either way.
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

// Dashboard panel -> Alerts ("Create alert" on a Logs/Metrics panel, DashboardPanelCard.svelte -
// the only producer). Traces has no alert condition kind to draft into (AlertConditionKind is
// LogCount/MetricThreshold/ExceptionCount only - see ADR-0020/ADR-0022), so there's no Traces
// variant here. Same "plain query params on the existing route, one-off/ephemeral hop" shape as
// the Metrics -> Logs/Traces links above - AlertRuleFormDialog.svelte's draft is consumed once
// (AlertsState.openCreateFromDraft) and never round-tripped back into a URL.

/** A Logs panel's condition, pre-filled into a new alert rule's LogCount block. */
export interface AlertDraftFromLogsPanel {
	kind: 'LogCount';
	name: string;
	services: string[];
	severityNumbers: number[];
	search: string;
}

/** A Metrics panel's condition, pre-filled into a new alert rule's MetricThreshold block - no
 *  `filter`/services counterpart: AlertRuleFormDialog's metric block has no filter UI to
 *  receive one today (see MetricAlertCondition.filter's own doc comment). */
export interface AlertDraftFromMetricsPanel {
	kind: 'MetricThreshold';
	name: string;
	metricName: string;
	metricType: MetricPointType;
}

export type AlertPanelDraft = AlertDraftFromLogsPanel | AlertDraftFromMetricsPanel;

function isMetricPointType(value: string | null): value is MetricPointType {
	return value === 'Gauge' || value === 'Sum' || value === 'Histogram';
}

export function buildAlertDeepLinkHref(draft: AlertPanelDraft): string {
	const params = new URLSearchParams({ kind: draft.kind, name: draft.name });
	if (draft.kind === 'LogCount') {
		if (draft.services.length) params.set('services', draft.services.join(','));
		if (draft.severityNumbers.length) params.set('severities', draft.severityNumbers.join(','));
		if (draft.search.trim()) params.set('search', draft.search.trim());
	} else {
		params.set('metricName', draft.metricName);
		params.set('metricType', draft.metricType);
	}
	return `/alerts?${params.toString()}`;
}

// Logs "context" view permalink (`?context=<eventId>&ts=<timestamp>`) - a click on
// LogContextSheet's "Copy link" button is the only producer. Same "plain query params on
// the existing route" shape as the Metrics -> Logs/Traces links above, but unlike those
// it's read back by the *same* page rather than only ever a cross-page hop: opening one of
// these links loads that specific event's context directly (LogsExplorerState.openContext),
// independent of - and without needing - the search filter that originally found the event.
// See LogContextRequest.cs's remarks for why the context query itself is unfiltered.

export interface ParsedLogContextDeepLink {
	eventId: string;
	/** ISO-8601, exactly as LogEventDto.timestamp serializes it - round-tripped verbatim, never reformatted, so it stays the exact sort-key value the anchor lookup needs. */
	timestamp: string;
}

export function buildLogContextDeepLinkHref(event: { eventId: string; timestamp: string }): string {
	const params = new URLSearchParams({ context: event.eventId, ts: event.timestamp });
	return `/?${params.toString()}`;
}

/** Parses `+page.svelte`'s (root, Logs) `?context=`/`?ts=` params - null when this isn't a context-permalink arrival (checked alongside `?view=`/the Metrics deep link, all mutually exclusive). */
export function parseLogContextDeepLinkParams(url: URL): ParsedLogContextDeepLink | null {
	const eventId = url.searchParams.get('context');
	const timestamp = url.searchParams.get('ts');
	if (!eventId || !timestamp) return null;
	return { eventId, timestamp };
}

/** Parses `routes/alerts/+page.svelte`'s deep-link params - null when this isn't a deep-link arrival (a direct visit, or the unrelated `?rule=<id>` history deep-link, checked separately by the caller). */
export function parseAlertDeepLinkParams(url: URL): AlertPanelDraft | null {
	const kind = url.searchParams.get('kind');
	if (kind !== 'LogCount' && kind !== 'MetricThreshold') return null;
	const name = url.searchParams.get('name') ?? '';
	if (kind === 'MetricThreshold') {
		const metricName = url.searchParams.get('metricName');
		const metricType = url.searchParams.get('metricType');
		if (!metricName || !isMetricPointType(metricType)) return null;
		return { kind, name, metricName, metricType };
	}
	const services = url.searchParams.get('services');
	const severities = url.searchParams.get('severities');
	return {
		kind,
		name,
		services: services ? services.split(',').filter(Boolean) : [],
		severityNumbers: severities
			? severities
					.split(',')
					.map(Number)
					.filter((n) => Number.isFinite(n))
			: [],
		search: url.searchParams.get('search') ?? ''
	};
}