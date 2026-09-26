// Per-panel visualization types for dashboard Metrics panels (roadmap's "Dashboard panel
// visualization types" item). `DashboardPanel.panelType` stays the *data source* (which
// Explorer's query a panel embeds); `visualization` is how that query's result is drawn,
// switchable in place without touching the query. Frontend-only, persisted inside the
// dashboard's opaque `layoutJson` like `thresholds`/`yAxisMin` - no backend/schema change.
// See docs-internal/adr/0059-dashboard-panel-visualizations.md (and 0061 for the histogram
// visualization and per-column table units).
//
// Only Metrics panels have alternatives: a Logs panel is a volume chart and a Traces panel a
// trace list, neither of which is a numeric series set these reshapes apply to.

import { isHistogramType, type MetricPointType, type MetricSeries, type MetricSeriesPoint } from '$lib/metrics-api';
import { formatAtScale, niceAxisTicks, resolveAxisScale, type AxisScale } from '$lib/metrics/axis';

/** One standalone reading (a Value panel's number, a table cell, a pie legend entry), scaled
 *  on its own magnitude - "1.2 s" next to "300 ms" - unlike a chart axis, where every tick
 *  shares one scale. */
export function formatValue(raw: number, unit: string | null | undefined): string {
	return formatAtScale(raw, resolveAxisScale(unit, Math.abs(raw)));
}

export type PanelVisualization = 'timeSeries' | 'bar' | 'stackedBar' | 'value' | 'pie' | 'table' | 'histogram';

export const PANEL_VISUALIZATIONS: readonly PanelVisualization[] = ['timeSeries', 'bar', 'stackedBar', 'value', 'pie', 'table', 'histogram'];

/** How a series' per-bucket values collapse into the one number a Value/Pie panel (and the
 *  Table's highlighted column) shows. */
export type PanelReducer = 'last' | 'avg' | 'sum' | 'min' | 'max';

export const PANEL_REDUCERS: readonly PanelReducer[] = ['last', 'avg', 'sum', 'min', 'max'];

/** Visualizations that collapse each series to a single number, and so read `reducer`. */
export function usesReducer(visualization: PanelVisualization): boolean {
	return visualization === 'value' || visualization === 'pie' || visualization === 'table';
}

/** Visualizations drawn against a Y axis, and so honour `yAxisMin`/`yAxisMax`. */
export function usesYAxis(visualization: PanelVisualization): boolean {
	return visualization === 'timeSeries' || visualization === 'bar' || visualization === 'stackedBar';
}

/** Lenient read of a stored value - anything unrecognised (a hand-edited import, a value
 *  from a newer build) falls back to the line chart every panel had before this existed. */
export function parseVisualization(raw: unknown): PanelVisualization {
	return PANEL_VISUALIZATIONS.includes(raw as PanelVisualization) ? (raw as PanelVisualization) : 'timeSeries';
}

/**
 * The reducer used when a panel hasn't picked one. A Sum metric's buckets are increments,
 * so adding them up answers "how many over the range"; every other shape (Gauge levels,
 * Histogram means, a formula's arbitrary result) is a level, where adding buckets together
 * would scale with the bucket count rather than mean anything - averaged instead.
 */
export function defaultReducer(resultType: MetricPointType | null): PanelReducer {
	return resultType === 'Sum' ? 'sum' : 'avg';
}

export function resolveReducer(raw: unknown, resultType: MetricPointType | null): PanelReducer {
	return PANEL_REDUCERS.includes(raw as PanelReducer) ? (raw as PanelReducer) : defaultReducer(resultType);
}

/**
 * The single plottable number in one bucket. Gauge/Sum carry it as `value`; a Histogram
 * bucket has no single value, so it uses the mean (`sum / count`) - the one Histogram
 * reading that stays meaningful when a reducer then averages/min/maxes it across buckets,
 * unlike a percentile (an average of p95s is not a p95). `null` = no data in this bucket.
 */
export function pointValue(point: MetricSeriesPoint, resultType: MetricPointType | null): number | null {
	if (isHistogramType(resultType)) {
		return point.sum != null && point.count != null && point.count > 0 ? point.sum / point.count : null;
	}
	return point.value;
}

export function reduceValues(values: readonly number[], reducer: PanelReducer): number | null {
	if (values.length === 0) return null;
	switch (reducer) {
		case 'last':
			return values[values.length - 1];
		case 'sum':
			return values.reduce((a, b) => a + b, 0);
		case 'avg':
			return values.reduce((a, b) => a + b, 0) / values.length;
		case 'min':
			return Math.min(...values);
		case 'max':
			return Math.max(...values);
	}
}

export function seriesLabel(series: MetricSeries): string {
	const attrs = Object.entries(series.attributes)
		.map(([k, v]) => `${k}=${v}`)
		.join(', ');
	return attrs ? `${series.serviceName} (${attrs})` : series.serviceName;
}

/** One series flattened to (time, value) pairs, oldest first, empty buckets dropped. */
export interface VizSeries {
	/** Full identity (`seriesLabel`) - stable across panels, so it's what picks the series' color. */
	label: string;
	/** What's actually shown - see `displayLabels`. */
	displayLabel: string;
	points: { time: number; value: number }[];
}

/**
 * Shorter labels for display, dropping whatever every series has in common: the service
 * name when they all share one, and the attribute key when every series is split by the
 * same single key (the usual group-by case) - `shop-api (route=/cart)` reads as `/cart`.
 * Anything less uniform keeps the full label rather than guessing what distinguishes them.
 */
export function displayLabels(series: readonly MetricSeries[]): string[] {
	if (series.length <= 1) return series.map(seriesLabel);
	const sameService = new Set(series.map((s) => s.serviceName)).size === 1;
	if (!sameService) return series.map(seriesLabel);
	const keySets = series.map((s) => Object.keys(s.attributes));
	const singleKey = keySets.every((k) => k.length === 1 && k[0] === keySets[0][0]);
	if (singleKey) return series.map((s) => Object.values(s.attributes)[0] || seriesLabel(s));
	return series.map((s) => {
		const attrs = Object.entries(s.attributes).map(([k, v]) => `${k}=${v}`).join(', ');
		return attrs || s.serviceName;
	});
}

/** Largest first by total magnitude, so a chart that caps its series count keeps the ones that matter. */
export function byMagnitude(series: readonly VizSeries[]): VizSeries[] {
	const magnitude = (s: VizSeries) => s.points.reduce((sum, p) => sum + Math.abs(p.value), 0);
	return [...series].sort((a, b) => magnitude(b) - magnitude(a));
}

export function toVizSeries(series: readonly MetricSeries[], resultType: MetricPointType | null): VizSeries[] {
	const labels = displayLabels(series);
	return series.map((s, i) => ({
		label: seriesLabel(s),
		displayLabel: labels[i],
		points: s.points
			.map((p) => ({ time: new Date(p.bucketStart).getTime(), value: pointValue(p, resultType) }))
			.filter((p): p is { time: number; value: number } => p.value != null && Number.isFinite(p.value))
			.sort((a, b) => a.time - b.time)
	}));
}

/**
 * Per-bucket sum across every series - the Value panel's input when a query returns more
 * than one series, so the headline number covers the whole query rather than an arbitrary
 * first series. Same "sum the series per bucket" collapse MetricChart's own comparison
 * overlay already applies (`totalsByBucket`).
 */
export function totalsByBucket(series: readonly VizSeries[]): number[] {
	const byTime = new Map<number, number>();
	for (const s of series) {
		for (const p of s.points) byTime.set(p.time, (byTime.get(p.time) ?? 0) + p.value);
	}
	return [...byTime.entries()].sort((a, b) => a[0] - b[0]).map(([, v]) => v);
}

export interface PieSlice {
	label: string;
	value: number;
	/** `true` for the folded "Other" slice. */
	other: boolean;
}

/**
 * Largest-first slices, capped at `maxSlices` named ones - the rest fold into one "Other"
 * slice rather than cycling the 5-colour categorical palette. Non-positive values are
 * dropped: a pie can't draw a negative share, and a zero one is invisible anyway.
 */
export function pieSlices(entries: readonly { label: string; value: number }[], maxSlices: number, otherLabel: string): PieSlice[] {
	const positive = entries.filter((e) => e.value > 0).sort((a, b) => b.value - a.value);
	if (positive.length <= maxSlices) return positive.map((e) => ({ ...e, other: false }));
	const named = positive.slice(0, maxSlices - 1).map((e) => ({ ...e, other: false }));
	const rest = positive.slice(maxSlices - 1).reduce((sum, e) => sum + e.value, 0);
	return [...named, { label: otherLabel, value: rest, other: true }];
}

/** SVG path for one pie slice between two fractions (0-1) of the full circle, clockwise from 12 o'clock. */
export function pieSlicePath(cx: number, cy: number, r: number, startFraction: number, endFraction: number): string {
	if (endFraction - startFraction >= 0.9999) {
		// A single full-circle slice: an arc whose start and end coincide draws nothing, so use two half-arcs.
		return `M ${cx} ${cy - r} A ${r} ${r} 0 1 1 ${cx} ${cy + r} A ${r} ${r} 0 1 1 ${cx} ${cy - r} Z`;
	}
	const point = (f: number) => {
		const angle = f * 2 * Math.PI - Math.PI / 2;
		return [cx + r * Math.cos(angle), cy + r * Math.sin(angle)];
	};
	const [x1, y1] = point(startFraction);
	const [x2, y2] = point(endFraction);
	const largeArc = endFraction - startFraction > 0.5 ? 1 : 0;
	return `M ${cx} ${cy} L ${x1} ${y1} A ${r} ${r} 0 ${largeArc} 1 ${x2} ${y2} Z`;
}

/** One value-distribution histogram bin: `[from, to)`, except the last bin, which also holds `to`. */
export interface HistogramBin {
	from: number;
	to: number;
	count: number;
}

/**
 * Value-distribution histogram (the `histogram` visualization): every bucket reading of
 * every series pooled, then counted into equal-width bins - "how often was the value in
 * this range", not "what was the value at this time". Bin edges are `niceAxisTicks`' round
 * values in the display scale ("0 / 100 / 200 ms", not "0 / 97.3 / 194.6"), aiming for
 * Sturges' ceil(log2 n) + 1 bins, clamped to 5..30 so a handful of readings still spreads
 * out and a long range doesn't turn into slivers. All readings equal = one bin holding them.
 */
export function histogramBins(values: readonly number[], unit: string | null): { bins: HistogramBin[]; scale: AxisScale } {
	const finite = values.filter((v) => Number.isFinite(v));
	if (finite.length === 0) return { bins: [], scale: resolveAxisScale(unit, 0) };
	const lo = Math.min(...finite);
	const hi = Math.max(...finite);
	const scale = resolveAxisScale(unit, Math.max(Math.abs(lo), Math.abs(hi)));
	if (lo === hi) return { bins: [{ from: lo, to: hi, count: finite.length }], scale };
	const target = Math.min(30, Math.max(5, Math.ceil(Math.log2(finite.length)) + 1));
	const edges = niceAxisTicks(lo, hi, scale, target).values;
	const step = edges[1] - edges[0];
	const bins: HistogramBin[] = edges.slice(0, -1).map((from, i) => ({ from, to: edges[i + 1], count: 0 }));
	for (const v of finite) {
		// The epsilon keeps a value sitting exactly on an edge (0.3 with a 0.1 step) out of the bin below it.
		const index = Math.min(bins.length - 1, Math.max(0, Math.floor((v - edges[0]) / step + 1e-9)));
		bins[index].count++;
	}
	return { bins, scale };
}

/**
 * A Table panel's per-column unit overrides (`DashboardPanel.columnUnits`), keyed by the
 * column's reducer. Lenient like `parseVisualization`: unknown keys and blank/non-string
 * values are dropped, so a column falls back to the metric's own unit.
 */
export function parseColumnUnits(raw: unknown): Partial<Record<PanelReducer, string>> {
	const out: Partial<Record<PanelReducer, string>> = {};
	if (raw == null || typeof raw !== 'object') return out;
	for (const [key, value] of Object.entries(raw)) {
		if (PANEL_REDUCERS.includes(key as PanelReducer) && typeof value === 'string' && value.trim()) {
			out[key as PanelReducer] = value.trim();
		}
	}
	return out;
}

function csvEscape(value: string): string {
	return /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}

/** RFC 4180 CSV. Values are written raw (unscaled, no unit suffix) so a spreadsheet can do arithmetic on them. */
export function toCsv(header: readonly string[], rows: readonly (readonly (string | number | null)[])[]): string {
	return [header, ...rows].map((row) => row.map((cell) => csvEscape(cell == null ? '' : String(cell))).join(',')).join('\r\n');
}
