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
import { formatAtScale, niceAxisTicks, parseYAxisScale, resolveAxisScale, type AxisScale, type YAxisScale } from '$lib/metrics/axis';

/** One standalone reading (a Value panel's number, a table cell, a pie legend entry), scaled
 *  on its own magnitude - "1.2 s" next to "300 ms" - unlike a chart axis, where every tick
 *  shares one scale. */
export function formatValue(raw: number, unit: string | null | undefined, decimals?: number): string {
	return formatAtScale(raw, resolveAxisScale(unit, Math.abs(raw)), decimals);
}

export type PanelVisualization = 'timeSeries' | 'bar' | 'value' | 'pie' | 'table' | 'histogram' | 'heatmap';

export const PANEL_VISUALIZATIONS: readonly PanelVisualization[] = ['timeSeries', 'bar', 'value', 'pie', 'table', 'histogram', 'heatmap'];

/** How a multi-series bar chart combines its series per bucket: side by side (`none`), piled
 *  up to the bucket's total (`normal`), or piled up and rescaled so every bucket fills
 *  0-100% (`percent`, showing each series' share). */
export type PanelStacking = 'none' | 'normal' | 'percent';

export const PANEL_STACKINGS: readonly PanelStacking[] = ['none', 'normal', 'percent'];

/** Visualizations that honour `stacking` - the bar chart, and the line chart (as stacked areas). */
export function usesStacking(visualization: PanelVisualization): boolean {
	return visualization === 'bar' || visualization === 'timeSeries';
}

/** A panel's effective stacking. A stored `stacking` wins; otherwise a legacy
 *  `visualization: 'stackedBar'` (the pre-`stacking` spelling of bar + normal) reads as
 *  `normal`, and everything else as `none`. */
export function parseStacking(raw: unknown, rawVisualization?: unknown): PanelStacking {
	if (PANEL_STACKINGS.includes(raw as PanelStacking)) return raw as PanelStacking;
	return rawVisualization === 'stackedBar' ? 'normal' : 'none';
}

/** How a series' per-bucket values collapse into the one number a Value/Pie panel (and the
 *  Table's highlighted column) shows. */
export type PanelReducer = 'last' | 'avg' | 'sum' | 'min' | 'max';

export const PANEL_REDUCERS: readonly PanelReducer[] = ['last', 'avg', 'sum', 'min', 'max'];

/** Visualizations that collapse each series to a single number, and so read `reducer`. */
export function usesReducer(visualization: PanelVisualization): boolean {
	return visualization === 'value' || visualization === 'pie' || visualization === 'table';
}

/** Visualizations that need each histogram point's per-bucket counts (`includeBuckets`) rather than the percentiles. */
export function needsBuckets(visualization: PanelVisualization): boolean {
	return visualization === 'heatmap';
}

/** Visualizations drawn against a Y axis, and so honour `yAxisMin`/`yAxisMax`. */
export function usesYAxis(visualization: PanelVisualization): boolean {
	return visualization === 'timeSeries' || visualization === 'bar';
}

/** Visualizations that draw a per-series legend, and so honour `legendPosition`/`seriesColors`
 *  (see `$lib/dashboards/legend.ts`). Value/Table/Histogram/Heatmap have no per-series marks to key. */
export function usesLegend(visualization: PanelVisualization): boolean {
	return visualization === 'timeSeries' || visualization === 'bar' || visualization === 'pie';
}

/** Visualizations that can switch to a log Y axis - the line chart only; a bar grows from
 *  zero, which a log axis has no place for. */
export function usesYAxisScale(visualization: PanelVisualization): boolean {
	return visualization === 'timeSeries';
}

/** A panel's own `yAxisScale` if set, else the one saved in its query (the Explorer view it
 *  was pinned from), else linear - see `DashboardPanel.yAxisScale`. */
export function effectivePanelYAxisScale(panel: { yAxisScale?: unknown; query: unknown }): YAxisScale {
	if (panel.yAxisScale != null) return parseYAxisScale(panel.yAxisScale);
	return parseYAxisScale((panel.query as { yAxisScale?: unknown } | null)?.yAxisScale);
}

/** Lenient read of a stored value - the retired `stackedBar` maps to `bar` (its stacking is
 *  recovered by `parseStacking`); anything unrecognised (a hand-edited import, a value
 *  from a newer build) falls back to the line chart every panel had before this existed. */
export function parseVisualization(raw: unknown): PanelVisualization {
	if (raw === 'stackedBar') return 'bar';
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
	/** The series' full identity (`seriesLabel`) - what a color override is keyed by. `null` for "Other". */
	key: string | null;
	/** `true` for the folded "Other" slice. */
	other: boolean;
}

/**
 * Largest-first slices, capped at `maxSlices` named ones - the rest fold into one "Other"
 * slice rather than cycling the 5-colour categorical palette. Non-positive values are
 * dropped: a pie can't draw a negative share, and a zero one is invisible anyway.
 */
export function pieSlices(entries: readonly { label: string; value: number; key: string }[], maxSlices: number, otherLabel: string): PieSlice[] {
	const positive = entries.filter((e) => e.value > 0).sort((a, b) => b.value - a.value);
	if (positive.length <= maxSlices) return positive.map((e) => ({ ...e, other: false }));
	const named = positive.slice(0, maxSlices - 1).map((e) => ({ ...e, other: false }));
	const rest = positive.slice(maxSlices - 1).reduce((sum, e) => sum + e.value, 0);
	return [...named, { label: otherLabel, value: rest, key: null, other: true }];
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

/** A heatmap's time-by-value grid: `cells[col][row]` observations in column `times[col]`, row `[edges[row], edges[row + 1])`. */
export interface HeatmapGrid {
	times: number[];
	edges: number[];
	cells: number[][];
	/** The largest cell, for scaling color. */
	peak: number;
}

const HEATMAP_MAX_ROWS = 40;

/**
 * The `heatmap` visualization's grid: each time bucket's histogram buckets (every series pooled
 * - like `histogram`, the question is "what values occurred when", not "which series") laid on
 * shared value rows. When the histograms' own bucket edges are few they become the rows as-is, so
 * a classic explicit histogram draws one row per bucket with exact counts; past
 * `HEATMAP_MAX_ROWS` distinct edges (exponential histograms, whose edges differ per scale) the
 * range is cut into that many rows - log-spaced when it is all positive - and each bucket's count
 * is split across the rows it overlaps in proportion to the overlap. `null` = no bucket data
 * (a non-histogram result, or a query that didn't set `includeBuckets`).
 */
export function heatmapGrid(series: readonly MetricSeries[]): HeatmapGrid | null {
	const byTime = new Map<number, { lower: number; upper: number; count: number }[]>();
	let lo = Infinity;
	let hi = -Infinity;
	const distinct = new Set<number>();
	for (const s of series) {
		for (const p of s.points) {
			if (!p.bucketLowers || !p.bucketUppers || !p.bucketCounts) continue;
			const time = new Date(p.bucketStart).getTime();
			const list = byTime.get(time) ?? [];
			for (let i = 0; i < p.bucketCounts.length; i++) {
				const lower = p.bucketLowers[i];
				const upper = p.bucketUppers[i];
				const count = p.bucketCounts[i];
				if (!(count > 0) || !Number.isFinite(lower) || !Number.isFinite(upper)) continue;
				list.push({ lower, upper, count });
				lo = Math.min(lo, lower);
				hi = Math.max(hi, upper);
				distinct.add(lower);
				distinct.add(upper);
			}
			if (list.length > 0) byTime.set(time, list);
		}
	}
	if (byTime.size === 0) return null;

	let edges = [...distinct].sort((a, b) => a - b);
	let logRows = false;
	if (edges.length > HEATMAP_MAX_ROWS + 1) {
		logRows = lo > 0;
		const rows = HEATMAP_MAX_ROWS;
		edges = Array.from({ length: rows + 1 }, (_, i) => (logRows ? lo * Math.pow(hi / lo, i / rows) : lo + ((hi - lo) * i) / rows));
	} else if (edges.length === 1) {
		// Every observation on one value (zero-width buckets): give the single row some height.
		edges = [edges[0], edges[0] + (edges[0] === 0 ? 1 : Math.abs(edges[0]) * 0.1)];
	}
	const rowCount = edges.length - 1;

	const times = [...byTime.keys()].sort((a, b) => a - b);
	const cells: number[][] = [];
	let peak = 0;
	for (const time of times) {
		const column = new Array<number>(rowCount).fill(0);
		for (const b of byTime.get(time)!) {
			if (b.upper <= b.lower) {
				// A zero-width bucket is a point: it lands in the row holding it.
				let row = edges.findIndex((e, i) => i < rowCount && b.lower >= e && b.lower < edges[i + 1]);
				if (row < 0) row = rowCount - 1;
				column[row] += b.count;
				continue;
			}
			const logSplit = logRows && b.lower > 0;
			const span = logSplit ? Math.log(b.upper / b.lower) : b.upper - b.lower;
			for (let r = 0; r < rowCount; r++) {
				const from = Math.max(b.lower, edges[r]);
				const to = Math.min(b.upper, edges[r + 1]);
				if (to <= from) continue;
				const share = (logSplit ? Math.log(to / from) : to - from) / span;
				column[r] += b.count * share;
			}
		}
		for (const v of column) peak = Math.max(peak, v);
		cells.push(column);
	}
	return { times, edges, cells, peak };
}
