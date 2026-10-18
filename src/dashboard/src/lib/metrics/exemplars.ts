// Exemplar dots drawn on the Metrics chart (ADR-0242). Pure placement helpers plus the loader; the SVG
// itself lives in MetricChart.svelte. Same shape as `#lib/releases/markers.ts`.

import { getMetricExemplars, type MetricExemplar, type MetricFilter, type MetricPointType } from '#lib/metrics-api.ts';
import { fractionInBuckets } from '#lib/releases/markers.ts';

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

/** Link to the exemplar's trace, focused on its span when it recorded one. */
export function exemplarTracePath(exemplar: Pick<MetricExemplar, 'traceId' | 'spanId'>): string {
	const base = `/traces/${encodeURIComponent(exemplar.traceId)}`;
	return exemplar.spanId ? `${base}?span=${encodeURIComponent(exemplar.spanId)}` : base;
}

/**
 * Exemplars for one metric over a window. Only histograms: a histogram's exemplar is a single
 * observation on the same axis as its percentile lines, whereas a counter's is an increment that
 * sits on no line the chart draws. A failure is an empty list: dots are decoration and must never
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
