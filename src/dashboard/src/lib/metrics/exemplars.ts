// Exemplar dots drawn on the Metrics chart (ADR-0242). Pure placement helpers plus the loader; the SVG
// itself lives in MetricChart.svelte. Same shape as `#lib/releases/markers.ts`.

import { getMetricExemplars, type MetricExemplar, type MetricFilter, type MetricPointType } from '#lib/metrics-api.ts';
import { fractionInBuckets } from '#lib/releases/markers.ts';
import { withBase } from '#lib/paths.ts';

/** Dots beyond this are noise on a chart a few hundred pixels wide. */
export const MAX_EXEMPLAR_DOTS = 100;

export interface ExemplarDot {
	exemplar: MetricExemplar;
	/** Exemplar time, epoch ms. */
	time: number;
	/** Position along the chart, 0..1. */
	fraction: number;
}

/**
 * Exemplars the chart can position, newest first. The x axis runs from the first to the last bucket's
 * *start*, but a bucket covers the `bucketMs` after its start, so an exemplar recorded inside the last
 * bucket (the newest, the one most worth seeing) lies past the axis end and is pinned to the right edge
 * rather than dropped. Value is not filtered here, the chart places (and clamps) it against its own y axis.
 */
export function placeExemplars(exemplars: readonly MetricExemplar[], bucketTimes: readonly number[], bucketMs: number): ExemplarDot[] {
	const placed: ExemplarDot[] = [];
	const last = bucketTimes[bucketTimes.length - 1];
	for (const exemplar of exemplars) {
		const time = new Date(exemplar.time).getTime();
		if (Number.isNaN(time)) continue;
		const fraction = bucketTimes.length >= 2 && time > last && time < last + bucketMs ? 1 : fractionInBuckets(time, bucketTimes);
		if (fraction !== null) placed.push({ exemplar, time, fraction });
	}
	return placed.sort((a, b) => b.time - a.time).slice(0, MAX_EXEMPLAR_DOTS);
}

/** One drawn exemplar, in the chart's viewBox coordinates (see `ExemplarDots.svelte`). */
export interface ExemplarMark {
	x: number;
	y: number;
	href: string;
	title: string;
	label: string;
}

/**
 * Whether an exemplar was recorded on a chart series: same service, and every attribute the series is keyed by
 * (all of the point's attributes when ungrouped, the single group-by key when grouped) has the series' value on
 * the exemplar. A key the exemplar lacks reads as '', which is how a grouped response names its "missing key" series.
 */
export function exemplarOnSeries(exemplar: MetricExemplar, series: { serviceName: string; attributes: Record<string, string> }): boolean {
	return (
		exemplar.serviceName === series.serviceName &&
		Object.entries(series.attributes).every(([key, value]) => (exemplar.dataPointAttributes[key] ?? '') === value)
	);
}

/**
 * The y of the drawn line at `x`, interpolated along its straight segments (chart pixel coordinates, `points` in x
 * order); null before the first or after the last point. For a counter or a formula the exemplar's own value is a
 * measurement on no axis the chart has (an increment, not a rate), so its dot rides the line itself, exactly on
 * the segment the pointer would follow, rather than floating at the containing bucket's height.
 */
export function yOnLine(points: readonly { x: number; y: number }[], x: number): number | null {
	for (let i = 0; i < points.length; i++) {
		if (points[i].x === x) return points[i].y;
		if (points[i].x > x) {
			if (i === 0) return null;
			const a = points[i - 1];
			const b = points[i];
			return a.y + ((b.y - a.y) * (x - a.x)) / (b.x - a.x);
		}
	}
	return null;
}

/** The trace link as an href (base path applied), for an anchor. */
export function exemplarHref(exemplar: Pick<MetricExemplar, 'traceId' | 'spanId'>): string {
	return withBase(exemplarTracePath(exemplar));
}

/** Link to the exemplar's trace, focused on its span when it recorded one. */
export function exemplarTracePath(exemplar: Pick<MetricExemplar, 'traceId' | 'spanId'>): string {
	const base = `/traces/${encodeURIComponent(exemplar.traceId)}`;
	return exemplar.spanId ? `${base}?span=${encodeURIComponent(exemplar.spanId)}` : base;
}

/**
 * Exemplars for one metric over a window. A failure is an empty list: dots are decoration and must never
 * break the chart they sit on.
 */
export async function loadExemplars(
	metric: { metricName: string; type: MetricPointType },
	filter: MetricFilter,
	signal?: AbortSignal
): Promise<MetricExemplar[]> {
	try {
		return await getMetricExemplars({ metricName: metric.metricName, type: metric.type, filter, limit: MAX_EXEMPLAR_DOTS }, signal);
	} catch {
		return [];
	}
}
