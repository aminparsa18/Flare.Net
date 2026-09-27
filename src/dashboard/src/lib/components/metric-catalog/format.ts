// Shared display helpers for the Metrics catalog table and its drill-down sheet.

import type { MetricPointType } from '$lib/metrics-api';
import * as m from '$lib/paraglide/messages';

// OTel names for the point types - technical terms, not translated (same as MetricPicker's
// badge labels).
export const TYPE_LABEL: Record<MetricPointType, string> = {
	Gauge: 'Gauge',
	Sum: 'Sum',
	Histogram: 'Histogram',
	ExponentialHistogram: 'Exp. Histogram'
};

// Rough thresholds for "worth a look" and "this will hurt": a few hundred series per metric is
// normal for a service with routes/status codes; thousands usually means an unbounded value
// (a user id, a URL with ids in it) recorded as an attribute.
const SERIES_WARNING = 1_000;
const SERIES_DANGER = 10_000;

export function cardinalityClass(count: number): string {
	if (count >= SERIES_DANGER) return 'text-destructive font-medium';
	if (count >= SERIES_WARNING) return 'text-warning';
	return '';
}

export function formatAgo(timestampMs: number, nowMs: number): string {
	const minutes = Math.floor((nowMs - timestampMs) / 60_000);
	if (minutes < 1) return m.metricCatalog_justNow();
	if (minutes < 60) return m.metricCatalog_minutesAgo({ minutes });
	return m.metricCatalog_hoursAgo({ hours: Math.floor(minutes / 60) });
}
