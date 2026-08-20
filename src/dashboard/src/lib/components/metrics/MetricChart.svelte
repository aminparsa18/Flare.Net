<script lang="ts">
	// Hand-rolled multi-line chart - no charting library, same "hand-roll it" precedent
	// VolumeChart/TraceWaterfall already set. Reuses VolumeChart's core technique
	// wholesale (viewBox SVG, array-index x positions rather than a real time scale,
	// pointer-move hover mapped to the nearest index, Tooltip.Root pinned open by
	// hover state) - the genuinely new part is multiple simultaneous lines instead of
	// one bar series, so see this file's own remarks for what that changes.
	import { fade } from 'svelte/transition';
	import * as Tooltip from '$lib/components/ui/tooltip';
	import * as Select from '$lib/components/ui/select';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Button } from '$lib/components/ui/button';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import { METRIC_SWITCH_FADE_MS } from '$lib/metrics/state.svelte';
	import { formatAtScale, niceAxisTicks, resolveAxisScale } from '$lib/metrics/axis';
	import { formatBucketWidthSeconds } from '$lib/logs/bucket-width';
	import { buildLogsDeepLinkHref, buildTracesDeepLinkHref } from '$lib/deep-links';
	import { TIME_RANGE_PRESETS, previousPeriodLabel, resolveTimeRange, previousPeriod } from '$lib/logs/time-range';
	import type { MetricSeries } from '$lib/metrics-api';

	const explorer = metricsExplorerContext.get();

	// Fixed categorical slot order (--chart-1..5, the `dataviz` skill's validated
	// palette - see layout.css's chart-1..5 comment) - never cycled past 5 series; a
	// 6th+ series folds into the "+N not shown" note below instead of reusing a hue,
	// per the skill's categorical-identity rule ("a 9th series is never a generated
	// hue").
	const SERIES_COLOR_VARS = ['--chart-1', '--chart-2', '--chart-3', '--chart-4', '--chart-5'] as const;
	const MAX_SERIES = SERIES_COLOR_VARS.length;

	// Histogram percentiles use fixed, meaning-carrying slots (not the general
	// series-identity order above) - p50/p90/p99 are the same three quantities on
	// every histogram, so their colors stay constant regardless of series count,
	// unlike Gauge/Sum lines whose color depends on their position in the series list.
	// p75/p95 aren't part of this map - they're separate single-line modes (see
	// HistogramMode below), not folded into the 3-line Percentiles view.
	const PERCENTILE_COLOR: Record<'p50' | 'p90' | 'p99', string> = {
		p50: 'var(--chart-1)',
		p90: 'var(--chart-2)',
		p99: 'var(--chart-4)'
	};
	// Shared slot for every single-computed-line histogram mode (mean/p75/p95/max) -
	// they're mutually exclusive (only one histogramMode is ever active), so there's
	// never a visual collision, and reusing one slot avoids spending --chart-5
	// (reserved for the 5th Gauge/Sum overlay series) on lines that never coexist.
	const SINGLE_LINE_COLOR = 'var(--chart-3)';

	// Alternate views for Sum/Histogram, picked via the small Select next to the
	// metadata row below - "how should this be aggregated", not "which series". All
	// are pure reshapes of the *already-fetched* data (rate = value / bucket width;
	// mean = sum / count; p75/p95/maxApprox are already returned per point - see
	// MetricSeriesPoint), so switching never re-queries.
	// Rate, not Sum, is the default view for Sum-typed metrics: bucket width here
	// tracks the selected time range (see intervalSeconds' own remarks), so a raw
	// per-bucket Sum for the same underlying counter reads differently depending on
	// which range happens to be selected - "100 exceptions" means something
	// different over 10s than over 1h. Dividing by bucket width makes the number
	// mean the same thing regardless of zoom level, which is what "exceptions/min"
	// operationally answers anyway. Sum stays one click away in the picker below.
	type SumMode = 'sum' | 'rate' | 'count';
	type HistogramMode = 'percentiles' | 'mean' | 'p75' | 'p95' | 'max';
	let sumMode = $state<SumMode>('rate');
	let histogramMode = $state<HistogramMode>('percentiles');

	const SUM_MODE_LABEL: Record<SumMode, string> = { sum: 'Sum', rate: 'Rate', count: 'Count' };
	// "Max (approx.)" everywhere it's user-visible (Select item, trigger, line
	// detail/tooltip) - MaxApprox is a bucket-boundary approximation, not the true
	// OTLP max (see MetricSeriesPoint.maxApprox), so the caveat travels with the
	// value rather than being a one-off mention.
	const HISTOGRAM_MODE_LABEL: Record<HistogramMode, string> = {
		percentiles: 'Percentiles',
		mean: 'Mean',
		p75: 'p75',
		p95: 'p95',
		max: 'Max (approx.)'
	};

	// Histogram modes period-over-period comparison supports - Mean (a single line,
	// same shape as Gauge/Sum) and Max (a "max of per-bucket maxApprox across the
	// period" is a mathematically valid aggregation, same reasoning Mean's weighted-
	// average already relies on). p75/p95 stay unavailable alongside Percentiles:
	// per-bucket percentiles can't be validly averaged/combined across a period the
	// way a mean or a max can, and the frontend never receives the raw bucket data a
	// true whole-period percentile would need anyway.
	const HISTOGRAM_COMPARABLE_MODES: readonly HistogramMode[] = ['mean', 'max'];

	// Full, unambiguous label - serviceName plus every attribute - used where
	// uniqueness matters more than brevity (the histogram series picker below,
	// which lists every series, not just the capped/visible set) and as the
	// per-legend-item tooltip's detail text (see compactSeriesLabel).
	function seriesLabel(series: MetricSeries): string {
		const attrs = Object.entries(series.attributes);
		if (attrs.length === 0) return series.serviceName;
		return `${series.serviceName} · ${attrs.map(([k, v]) => `${k}=${v}`).join(', ')}`;
	}

	// Compact legend/tooltip label: shows only what actually distinguishes this
	// series from the others *currently on the chart*, not the full dimension
	// set - with many series sharing serviceName and only one attribute varying
	// (e.g. 15 error.type values on the same service), repeating
	// "log-generator · error.type=" on every legend line is pure noise, and it's
	// the case that gets worse the more series-defining attributes a metric
	// carries. Real "group by a chosen attribute" (collapsing series server-side,
	// via MetricQueryRequest.groupByAttributeKey and MetricsToolbar's "Group by"
	// picker) now exists as a separate feature - this function still does its own,
	// independent job on top of it: compacting labels within whatever series set
	// the request actually produced, grouped or not. Grouped series already arrive
	// with a smaller (often single-entry) attributes map, so this needs no branch
	// for which mode produced them.
	function compactSeriesLabel(series: MetricSeries, visible: MetricSeries[]): string {
		if (visible.length <= 1) return seriesLabel(series);

		const serviceNameVaries = new Set(visible.map((s) => s.serviceName)).size > 1;
		const attrKeys = [...new Set(visible.flatMap((s) => Object.keys(s.attributes)))];
		const varyingKeys = attrKeys.filter((k) => new Set(visible.map((s) => s.attributes[k] ?? '')).size > 1);

		const parts: string[] = [];
		if (serviceNameVaries) parts.push(series.serviceName);
		if (varyingKeys.length === 1) {
			// Sole distinguishing dimension - the bare value ("InvalidOperationException")
			// reads better than the key-prefixed form repeated on every line. `||`, not
			// `??`: a grouped response's missing-key series (see
			// MetricSeriesQueryBuilder's remarks) surfaces as a present key with an empty
			// string value, not a missing one, so `??` alone would render a blank label -
			// `||` catches both "key absent" and "key present but empty" the same way.
			parts.push(series.attributes[varyingKeys[0]] || '(none)');
		} else {
			parts.push(...varyingKeys.map((k) => `${k}=${series.attributes[k] || '(none)'}`));
		}
		// Nothing varies (shouldn't happen - the backend groups series by distinct
		// attribute combinations, see MetricSeriesQueryBuilder's remarks) - fall
		// back to the full label rather than an empty legend entry.
		return parts.length > 0 ? parts.join(' · ') : seriesLabel(series);
	}

	// explorer.resultType, not explorer.selected?.type - `selected` updates the instant
	// a different metric is clicked (so the header/picker highlight update right away),
	// but `series` itself stays pinned to the *previous* metric's (differently-shaped)
	// points for the whole METRIC_SWITCH_FADE_MS deferral (see
	// MetricsExplorerState.#deferredReset's remarks). Keying the branch that decides
	// how to interpret `series` off the live target type, instead of what `series`
	// actually *is*, would misinterpret one type's points as the other's mid-outro -
	// a second, independent route to the same "content changes before the fade even
	// starts" bug the deferral exists to fix.
	const isHistogram = $derived(explorer.resultType === 'Histogram');
	const isSum = $derived(explorer.resultType === 'Sum');

	// Comparison mode supports Gauge/Sum (always) and Histogram's Mean/Max views
	// (see HISTOGRAM_COMPARABLE_MODES) - not Percentiles/p75/p95, which would need
	// N dashed-vs-solid lines for a real overlay (more clutter than the feature is
	// worth) and can't be validly averaged across a period anyway (unlike Mean/Max).
	// The chart doesn't silently ignore that case, though (a real UX gap in the first
	// cut of this feature) - see the header's own "switch to Mean or Max" note below.
	//
	// Keyed on `resultCompareEnabled` (what `series`/`previousSeries` were actually
	// fetched with), not the live `filter.compareEnabled` the toolbar switch shows -
	// see resultCompareEnabled's own remarks. histogramMode/isHistogram/selected don't
	// need the same lag: switching Percentiles<->Mean never re-queries (both are
	// already in every fetched point - see buildLines' own remarks), and a metric
	// switch clears series/previousSeries synchronously (MetricsExplorerState.
	// #resetForNewMetric) rather than leaving stale data to be inconsistent about.
	const compareActive = $derived(
		explorer.resultCompareEnabled &&
			(!isHistogram || HISTOGRAM_COMPARABLE_MODES.includes(histogramMode)) &&
			explorer.selected != null
	);
	const compareUnavailable = $derived(
		explorer.filter.compareEnabled && isHistogram && !HISTOGRAM_COMPARABLE_MODES.includes(histogramMode)
	);

	// "View related logs"/"View traces" - cross-links into Logs/Traces pre-filtered to
	// this metric's service and the explorer's current time range (see $lib/deep-links.ts),
	// so Metrics -> Logs -> Traces reads as one system rather than three unrelated pages.
	// The Logs link also narrows to a specific attribute, but only when it's unambiguous:
	// exactly one series charted, with exactly one attribute (e.g. dotnet.exceptions
	// isolated to one error.type). A multi-series or multi-attribute metric still links by
	// service alone - still useful, just not falsely asserting one line's specific value
	// applies to the whole metric.
	const logsHref = $derived.by(() => {
		if (!explorer.selected) return null;
		const single = explorer.series.length === 1 ? explorer.series[0] : null;
		const attrs = single ? Object.entries(single.attributes) : [];
		return buildLogsDeepLinkHref({
			serviceName: explorer.selected.serviceName,
			timeRangePreset: explorer.filter.timeRangePreset,
			attribute: attrs.length === 1 ? { key: attrs[0][0], value: attrs[0][1] } : undefined
		});
	});

	const tracesHref = $derived.by(() =>
		explorer.selected
			? buildTracesDeepLinkHref({ serviceName: explorer.selected.serviceName, timeRangePreset: explorer.filter.timeRangePreset })
			: null
	);

	// Histogram: chart one series' distribution (p50/p90/p99) at a time, picked below
	// - N series x 3 percentile lines each would be unreadable, and "look at this
	// instrument's latency shape" is a per-series question anyway. Gauge/Sum instead
	// overlay every (capped) series as its own line - those are single comparable
	// values, which is the actual point of overlaying them.
	let histogramSeriesIndex = $state(0);

	$effect(() => {
		// Reset the histogram series picker and the aggregation-mode toggles whenever
		// the tracked metric changes underneath it - same "abort/reset on reselect"
		// idiom SpanDetailSheet uses for its own local, view-only state. Resetting both
		// modes unconditionally (not just the one matching the new metric's type) is
		// simplest and never wrong - the unused one just sits at its default until it's
		// ever relevant again.
		void explorer.selected;
		histogramSeriesIndex = 0;
		sumMode = 'rate';
		histogramMode = 'percentiles';
	});

	const visibleSeries = $derived(
		isHistogram ? explorer.series.slice(histogramSeriesIndex, histogramSeriesIndex + 1) : explorer.series.slice(0, MAX_SERIES)
	);
	// Not applicable in comparison mode - buildComparisonLines sums *every* series
	// (uncapped), so there's nothing "not shown" to warn about there.
	const hiddenSeriesCount = $derived(
		isHistogram || compareActive ? 0 : Math.max(0, explorer.series.length - MAX_SERIES)
	);

	interface PlotPoint {
		// Epoch ms, not the API's raw bucketStart string - the x-axis identity/equality
		// key (bucketTimes/bucketIndexOf/pointAtHover below all key off this), so it has
		// to be something comparison mode's shifted "Previous" points can produce
		// *exactly* the same way normal points do. A string round-trip through
		// `Date.toISOString()` can't guarantee that: the API's DateTimeOffset JSON omits
		// fractional seconds and uses "+00:00" ("2026-08-19T10:00:00+00:00"), while
		// `toISOString()` always emits ".000Z" - same instant, never string-equal. A
		// bug this file actually shipped with (comparison mode's Previous line never
		// lining up with Current) before this comment existed - numeric time sidesteps
		// the whole format-matching class of bug rather than just this one instance.
		time: number;
		raw: number;
	}
	interface LineSpec {
		color: string;
		// Compact - what the wrapped legend row and chart's own crosshair-adjacent
		// space can afford to show. See `detail` for the full picture.
		label: string;
		// Full, unambiguous identity (serviceName + every attribute for a
		// Gauge/Sum series; itself for percentile/mean, which have no series
		// ambiguity since only one series is charted at a time) - shown in the
		// per-legend-item tooltip so compacting `label` never actually loses
		// information, just hides it until asked for.
		detail: string;
		// Set only by the comparison-mode "Previous" line (see buildComparisonLines) -
		// dashed stroke is the one visual distinction it needs beyond its muted color;
		// every other line leaves this unset (solid).
		dashed?: boolean;
		points: PlotPoint[];
	}

	function buildLines(): LineSpec[] {
		if (isHistogram) {
			const series = visibleSeries[0];
			if (!series) return [];
			if (histogramMode === 'mean') {
				return [
					{
						color: SINGLE_LINE_COLOR,
						label: 'mean',
						detail: 'mean',
						points: series.points
							.filter((p) => p.sum != null && p.count != null && p.count > 0)
							.map((p) => ({ time: new Date(p.bucketStart).getTime(), raw: p.sum! / p.count! }))
					}
				];
			}
			if (histogramMode === 'p75' || histogramMode === 'p95') {
				const key = histogramMode;
				return [
					{
						color: SINGLE_LINE_COLOR,
						label: key,
						detail: key,
						points: series.points
							.filter((p) => p[key] != null)
							.map((p) => ({ time: new Date(p.bucketStart).getTime(), raw: p[key]! }))
					}
				];
			}
			if (histogramMode === 'max') {
				return [
					{
						color: SINGLE_LINE_COLOR,
						label: HISTOGRAM_MODE_LABEL.max,
						detail: HISTOGRAM_MODE_LABEL.max,
						points: series.points
							.filter((p) => p.maxApprox != null)
							.map((p) => ({ time: new Date(p.bucketStart).getTime(), raw: p.maxApprox! }))
					}
				];
			}
			return (['p50', 'p90', 'p99'] as const).map((key) => ({
				color: PERCENTILE_COLOR[key],
				label: key,
				detail: key,
				points: series.points
					.filter((p) => p[key] != null)
					.map((p) => ({ time: new Date(p.bucketStart).getTime(), raw: p[key]! }))
			}));
		}

		return visibleSeries.map((series, i) => ({
			color: `var(${SERIES_COLOR_VARS[i]})`,
			label: compactSeriesLabel(series, visibleSeries),
			detail: seriesLabel(series),
			points: series.points
				.filter((p) => (isSum && sumMode === 'count' ? p.count != null : p.value != null))
				.map((p) => ({
					time: new Date(p.bucketStart).getTime(),
					raw: isSum && sumMode === 'count' ? p.count! : rateDivisor ? p.value! / rateDivisor : p.value!
				}))
		}));
	}

	// Sums every series' value at each bucket into one total - the same reduction
	// buildComparisonLines does for both periods, extracted since the percentage
	// summary needs the plain (pre-rate) totals too, independent of any one line's
	// x-position handling. Keyed by epoch ms (see PlotPoint.time's remarks), not the
	// API's bucketStart string.
	function totalsByBucket(series: MetricSeries[]): Map<number, number> {
		const totals = new Map<number, number>();
		for (const s of series) {
			for (const p of s.points) {
				if (p.value == null) continue;
				const time = new Date(p.bucketStart).getTime();
				totals.set(time, (totals.get(time) ?? 0) + p.value);
			}
		}
		return totals;
	}

	function sortedPoints(totals: Map<number, number>, shiftMs = 0): PlotPoint[] {
		return [...totals.entries()]
			.sort(([a], [b]) => a - b)
			.map(([time, total]) => ({ time: time + shiftMs, raw: rateDivisor ? total / rateDivisor : total }));
	}

	// Same identity MetricSeriesQueryBuilder's own SeriesKey is (ServiceName +
	// DataPointAttributes) - pairs one series to its previous-period counterpart by
	// what it *is*, not by array position (current/previous series lists aren't
	// guaranteed to line up index-for-index - e.g. an error.type that only started
	// appearing this period).
	function matchingSeries(series: MetricSeries, against: MetricSeries[]): MetricSeries | null {
		return against.find((s) => s.serviceName === series.serviceName && JSON.stringify(s.attributes) === JSON.stringify(series.attributes)) ?? null;
	}

	function meanPoints(series: MetricSeries | null, shiftMs = 0): PlotPoint[] {
		if (!series) return [];
		return series.points
			.filter((p) => p.sum != null && p.count != null && p.count > 0)
			.map((p) => ({ time: new Date(p.bucketStart).getTime() + shiftMs, raw: p.sum! / p.count! }));
	}

	// Mean's comparison-mode counterpart for the Max view - same shape, maps
	// maxApprox instead of sum/count.
	function maxApproxPoints(series: MetricSeries | null, shiftMs = 0): PlotPoint[] {
		if (!series) return [];
		return series.points
			.filter((p) => p.maxApprox != null)
			.map((p) => ({ time: new Date(p.bucketStart).getTime() + shiftMs, raw: p.maxApprox! }));
	}

	// Picks meanPoints vs maxApproxPoints for the Histogram case - buildComparisonLines
	// and comparePercent both need this same choice, so it's factored out once.
	function histogramComparePoints(series: MetricSeries | null, shiftMs = 0): PlotPoint[] {
		return histogramMode === 'max' ? maxApproxPoints(series, shiftMs) : meanPoints(series, shiftMs);
	}

	/**
	 * Comparison mode's two lines, "Current" and "Previous", built one of two ways
	 * depending on type - each matching how that type's *normal* (non-compare) mode
	 * already scopes its data, so compare mode never shows a different slice than what
	 * was already on screen before it was switched on:
	 *
	 * - Gauge/Sum: every series summed into one total per bucket (not paired 1:1) -
	 *   normal mode already overlays every (capped) series as its own line, and the
	 *   user's own request was "Exceptions: +34%", one headline number, not N current
	 *   lines next to N previous ones (which doubles an already-busy legend - see item
	 *   6/7's compactSeriesLabel). For a single-series metric this reduces to exactly
	 *   that one series' current/previous anyway.
	 * - Histogram (Mean/Max views only - see compareActive/compareUnavailable/
	 *   HISTOGRAM_COMPARABLE_MODES): normal mode already restricts to the one series
	 *   picked via histogramSeriesIndex, so compare mode does too - paired to its
	 *   previous-period counterpart via matchingSeries, not blended with any other
	 *   series. histogramComparePoints picks mean-vs-max shaping to match whichever
	 *   of the two is active.
	 *
	 * Previous's `time` values are shifted forward by one full period (in shiftMs, as a
	 * number - see PlotPoint.time's remarks on why not a re-stringified timestamp) so
	 * they land on the *same* x-position as their current-period counterpart (an
	 * overlay, not a second, earlier-in-time set of points) - see previousPeriod's own
	 * remarks for why this is exact (bucket boundaries are anchored to absolute time,
	 * and the shift is exactly one period's duration).
	 */
	function buildComparisonLines(): LineSpec[] {
		// The preset's fixed duration, not a fresh resolveTimeRange() - same value
		// either way (a preset's from/to always differ by exactly its durationMs,
		// whatever "now" happens to be at call time), but this is the one that's
		// actually deterministic rather than incidentally so.
		const shiftMs = TIME_RANGE_PRESETS.find((p) => p.value === explorer.filter.timeRangePreset)?.durationMs ?? 0;
		const currentPoints = isHistogram
			? histogramComparePoints(visibleSeries[0] ?? null)
			: sortedPoints(totalsByBucket(explorer.series));
		const previousPoints = isHistogram
			? histogramComparePoints(visibleSeries[0] ? matchingSeries(visibleSeries[0], explorer.previousSeries) : null, shiftMs)
			: sortedPoints(totalsByBucket(explorer.previousSeries), shiftMs);
		return [
			{ color: 'var(--chart-1)', label: 'Current', detail: 'Current', points: currentPoints },
			{ color: 'var(--muted-foreground)', label: 'Previous', detail: 'Previous', dashed: true, points: previousPoints }
		];
	}

	const rateDivisor = $derived(isSum && sumMode === 'rate' ? explorer.intervalSeconds : null);
	const lines = $derived(compareActive ? buildComparisonLines() : buildLines());

	// Changes exactly when the chart's *shape* changes - a different metric, or
	// comparison mode actually switching on/off (compareActive, already gated on
	// resultCompareEnabled so this never fires mid-fetch - see its own remarks). Keys
	// the {#key} crossfade below: a plain data refresh on the same metric/mode (a
	// different time range, an added service) morphs the existing lines in place with
	// no fade at all, which reads as smoothly updated rather than "reloaded"; a real
	// shape change (N per-series lines <-> 2 aggregate lines, or metric A -> metric B)
	// gets a quick fade instead of an instant swap.
	const chartKey = $derived(`${explorer.selected?.metricName ?? ''}|${explorer.selected?.serviceName ?? ''}|${compareActive}`);

	// Same reduction buildComparisonLines' two lines are built from, but over the whole
	// period at once rather than per-bucket - the percentage summary is one number, not
	// a time series. Independent of `rateDivisor` for the Gauge/Sum case (a plain sum,
	// not a rate) since a percentage change is identical either way - rate divides both
	// totals by the same bucket width, which cancels out of the ratio. null when
	// there's nothing to compare (comparison mode isn't active, or the previous period
	// has no data at all - can't divide by zero, and "some number vs no baseline" isn't
	// a percentage).
	const comparePercent = $derived.by((): number | 'new' | null => {
		if (!compareActive) return null;
		let currentTotal: number;
		let previousTotal: number;
		if (isHistogram) {
			const weightedMean = (series: MetricSeries | null) => {
				if (!series) return null;
				let sum = 0;
				let count = 0;
				for (const p of series.points) {
					if (p.sum == null || p.count == null) continue;
					sum += p.sum;
					count += p.count;
				}
				return count > 0 ? sum / count : null;
			};
			// "Max of per-bucket maxApprox across the whole period" - a valid aggregation
			// for Max (unlike averaging percentiles, which weightedMean above wouldn't
			// even make sense for), mirroring why HISTOGRAM_COMPARABLE_MODES includes it.
			const periodMax = (series: MetricSeries | null) => {
				if (!series) return null;
				const values = series.points.map((p) => p.maxApprox).filter((v): v is number => v != null);
				return values.length > 0 ? Math.max(...values) : null;
			};
			const aggregate = histogramMode === 'max' ? periodMax : weightedMean;
			const current = visibleSeries[0] ?? null;
			currentTotal = aggregate(current) ?? 0;
			previousTotal = aggregate(current ? matchingSeries(current, explorer.previousSeries) : null) ?? 0;
		} else {
			const sum = (series: MetricSeries[]) => series.flatMap((s) => s.points).reduce((total, p) => total + (p.value ?? 0), 0);
			currentTotal = sum(explorer.series);
			previousTotal = sum(explorer.previousSeries);
		}
		if (previousTotal === 0) return currentTotal === 0 ? null : 'new';
		return ((currentTotal - previousTotal) / previousTotal) * 100;
	});

	const compareChangeText = $derived.by(() => {
		if (comparePercent === null) return null;
		const periodLabel = previousPeriodLabel(explorer.filter.timeRangePreset);
		if (comparePercent === 'new') return `new (no data in ${periodLabel})`;
		const sign = comparePercent > 0 ? '+' : '';
		return `${sign}${comparePercent.toFixed(0)}% vs ${periodLabel}`;
	});

	// "previous 24 hours" names the *duration* being compared, not which 24 hours that
	// actually is - answers "what does previous period mean" concretely, on hover,
	// rather than requiring a click into a real date-range picker Metrics doesn't have
	// (only fixed presets - see MetricsToolbar's own remarks).
	const compareRangeDetail = $derived.by(() => {
		if (!compareChangeText) return null;
		const range = resolveTimeRange(explorer.filter.timeRangePreset);
		if (!range) return null;
		const previous = previousPeriod(range);
		const fmt = (iso: string) =>
			new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
		return `Current: ${fmt(range.from)} – ${fmt(range.to)}\nPrevious: ${fmt(previous.from)} – ${fmt(previous.to)}`;
	});

	// Shared x-domain: every distinct bucket across the visible lines, in order - same
	// "array-index position, not a real time scale" simplification VolumeChart already
	// uses (see its own remarks). Known v1 simplification: a line missing a bucket
	// (no data point at that index) draws straight through to its next point rather
	// than breaking - acceptable for the sparse-gap case this is meant to handle, not
	// meant to imply interpolated data across a large hole. Epoch ms (see
	// PlotPoint.time's remarks), not the API's bucketStart string.
	const bucketTimes = $derived([...new Set(lines.flatMap((l) => l.points.map((p) => p.time)))].sort((a, b) => a - b));
	const bucketIndexOf = $derived(new Map(bucketTimes.map((t, i) => [t, i])));

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 180;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	// Out, then in - see the {#key chartKey} block's own remarks on why sequential,
	// not overlapping. Also animateHeight's own duration, so the container's resize
	// keeps pace with the full out+in sequence instead of finishing early. Imported
	// from MetricsExplorerState, not a second hand-picked number here - it's also
	// exactly how long #deferredReset waits before clearing series/previousSeries on
	// a metric switch (see its own remarks), so the *outgoing* chart's data doesn't
	// change out from under it before its own out-transition even starts. Two
	// independently-chosen constants that happened to match would drift the moment
	// either one changed.
	const FADE_MS = METRIC_SWITCH_FADE_MS;

	const peakValue = $derived(Math.max(0, ...lines.flatMap((l) => l.points.map((p) => p.raw))));

	// Rate mode changes the unit, not just the numbers - a Sum declared "By" reads as
	// "By/s" once every value has been divided by the bucket width. axis.ts already
	// knows how to split/format a "<unit>/s" rate unit (composing it back this way is
	// simplest: MetricNameInfo has no separate "rate unit" of its own to read). Count
	// mode is a plain sample count, not the metric's declared unit at all - showing it
	// labeled "ms"/"By" would be actively wrong (unlike Rate, which composes cleanly),
	// so it gets no unit rather than an inherited, misleading one.
	const displayUnit = $derived(
		isSum && sumMode === 'count'
			? null
			: isSum && sumMode === 'rate'
				? `${explorer.selected?.unit ?? ''}/s`
				: explorer.selected?.unit
	);

	// One scale for the whole chart (e.g. "ms"), picked from the data's raw peak so
	// every tick/tooltip value reads in the same unit instead of each re-picking its
	// own ("40 ms" next to "0.03 s").
	const axisScale = $derived(resolveAxisScale(displayUnit, peakValue));

	// Round the axis up to a "nice" ceiling in the *displayed* scale (e.g. peak 37ms ->
	// ticks 0/10/20/30/40 ms) rather than scaling exactly to the data's raw peak, so
	// the top gridline lands on a number a human would actually pick - see axis.ts.
	const ticks = $derived(niceAxisTicks(peakValue, axisScale));
	const maxValue = $derived(Math.max(1e-9, ticks.max));

	function xFor(time: number): number {
		const count = bucketTimes.length;
		if (count <= 1) return CHART_WIDTH / 2;
		return (bucketIndexOf.get(time)! / (count - 1)) * CHART_WIDTH;
	}

	function yFor(raw: number): number {
		return BASELINE_Y - (raw / maxValue) * (BASELINE_Y - PEAK_Y);
	}

	function pathFor(points: PlotPoint[]): string {
		return points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${xFor(p.time)} ${yFor(p.raw)}`).join(' ');
	}

	let hoverIndex = $state<number | null>(null);

	function handlePointerMove(e: PointerEvent) {
		if (bucketTimes.length === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		const rect = svg.getBoundingClientRect();
		const fraction = (e.clientX - rect.left) / rect.width;
		hoverIndex = Math.min(bucketTimes.length - 1, Math.max(0, Math.round(fraction * (bucketTimes.length - 1))));
	}

	function pointAtHover(line: LineSpec): PlotPoint | undefined {
		if (hoverIndex === null) return undefined;
		return line.points.find((p) => p.time === bucketTimes[hoverIndex!]);
	}

	function formatBucketTime(time: number): string {
		return new Date(time).toLocaleString(undefined, {
			hour12: false,
			month: 'short',
			day: 'numeric',
			hour: '2-digit',
			minute: '2-digit'
		});
	}

	// Tooltip values share the axis's scale (not each point re-picking its own) so a
	// hovered point never reads in a different unit than the gridline it sits next to.
	function formatValue(n: number): string {
		return formatAtScale(n, axisScale);
	}

	/**
	 * Smoothly resizes this element whenever its content's natural height changes,
	 * instead of snapping - the `{#key chartKey}` fade below only animates opacity,
	 * which doesn't stop the *container* from instantly jumping to the new height the
	 * moment swapped-in content mounts. That jump was barely noticeable for Histogram
	 * (3 percentile lines vs. 2 compare lines - a small delta) but real for Sum/Gauge
	 * with several series (a wrapped multi-row legend + a "+N more series" note
	 * collapsing down to a plain 2-line "Current"/"Previous" legend - a much bigger
	 * one). A FLIP-style animation via ResizeObserver + the Web Animations API: capture
	 * the height *before* this fires (the browser has already resized the box by the
	 * time the observer callback runs), then explicitly animate from that captured
	 * value to the new one, over the same FADE_MS*2 the out+in fade sequence takes -
	 * so the box finishes resizing right as the new content finishes fading in, not
	 * mid-fade or well after it. No shared `$lib/actions` home yet for this - inlined
	 * until a second consumer needs it (same "no natural shared-utils home for a
	 * two-line helper yet" call context.ts's own generic-context helper makes).
	 */
	function animateHeight(node: HTMLElement) {
		let prevHeight = node.getBoundingClientRect().height;
		const observer = new ResizeObserver(() => {
			const nextHeight = node.getBoundingClientRect().height;
			if (Math.abs(nextHeight - prevHeight) > 0.5) {
				node.animate([{ height: `${prevHeight}px` }, { height: `${nextHeight}px` }], { duration: FADE_MS * 2, easing: 'ease' });
			}
			prevHeight = nextHeight;
		});
		observer.observe(node);
		return {
			destroy() {
				observer.disconnect();
			}
		};
	}
</script>

<div class="flex min-h-0 flex-1 flex-col">
	{#if !explorer.selected}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>No metric selected</Empty.Title>
				<Empty.Description>Pick a metric from the list to chart it.</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div class="flex shrink-0 items-center justify-between gap-2 border-b px-4 py-3">
			<div class="min-w-0">
				<h2 class="truncate text-sm font-medium">{explorer.selected.metricName}</h2>
				{#if explorer.selected.description}
					<p class="text-muted-foreground truncate text-xs">{explorer.selected.description}</p>
				{/if}
				<!-- Only once a query has actually completed for the currently-selected metric
				     (see intervalSeconds' own remarks) - otherwise this would flash the
				     previous metric's series count/interval for a moment on every switch. -->
				{#if explorer.intervalSeconds !== null}
					<div class="text-muted-foreground mt-1 flex flex-wrap items-center gap-x-1.5 text-xs">
						<!-- The type itself is never a plain label here for Sum/Histogram - it
						     doubles as the aggregation-mode picker (Sum/Rate, Percentiles/Mean).
						     A real Select, not a styled-to-look-clickable badge: badges elsewhere
						     in this app (the picker list's own Sum/Histogram tags included) are
						     inert, so overloading one with a click handler here would be an
						     undiscoverable, inconsistent affordance - this reuses the same
						     Select.Trigger pattern the series picker to the right already uses. -->
						{#if isSum}
							<Select.Root type="single" value={sumMode} onValueChange={(v) => v && (sumMode = v as SumMode)}>
								<Select.Trigger class="w-auto">
									{SUM_MODE_LABEL[sumMode]}
								</Select.Trigger>
								<Select.Content>
									<Select.Item value="sum" label="Sum" />
									<Select.Item value="rate" label="Rate" />
									<Select.Item value="count" label="Count" />
								</Select.Content>
							</Select.Root>
						{:else if isHistogram}
							<Select.Root
								type="single"
								value={histogramMode}
								onValueChange={(v) => v && (histogramMode = v as HistogramMode)}
							>
								<Select.Trigger class="w-auto">
									{HISTOGRAM_MODE_LABEL[histogramMode]}
								</Select.Trigger>
								<Select.Content>
									<Select.Item value="percentiles" label="Percentiles" />
									<Select.Item value="mean" label="Mean" />
									<Select.Item value="p75" label="p75" />
									<Select.Item value="p95" label="p95" />
									<Select.Item value="max" label={HISTOGRAM_MODE_LABEL.max} />
								</Select.Content>
							</Select.Root>
						{:else}
							<!-- resultType, not explorer.selected.type - same reasoning as
							     isHistogram/isSum above; keeps this consistent with which of the
							     three branches actually rendered. -->
							<span>{explorer.resultType}</span>
						{/if}
						<span aria-hidden="true">·</span>
						<!-- "(summed)" only for Gauge/Sum comparison - the chart is showing 2
						     aggregate-across-every-series lines then (see buildComparisonLines),
						     not one per series, so this count would otherwise look inconsistent
						     with what's actually drawn. Histogram compare doesn't get this -
						     it's still the one series histogramSeriesIndex already picks, same
						     as outside comparison mode, never a cross-series aggregate. -->
						<span>{explorer.series.length} series{compareActive && !isHistogram ? ' (summed)' : ''}</span>
						<span aria-hidden="true">·</span>
						<span>{formatBucketWidthSeconds(explorer.intervalSeconds)} interval</span>
						{#if compareChangeText}
							<span aria-hidden="true">·</span>
							<Tooltip.Provider>
								<Tooltip.Root>
									<Tooltip.Trigger>
										{#snippet child({ props })}
											<!-- Deliberately no color-coding (green/red) - "up" isn't
											     universally good or bad (exceptions vs. requests/sec
											     read oppositely), so a neutral, "subtle" number per the
											     request this shipped from, not a judgment. Hoverable:
											     "previous 24 hours" names a duration, not which 24 hours
											     - the tooltip answers that with the actual compared
											     dates (see compareRangeDetail) instead of requiring a
											     real date-range picker Metrics doesn't have. -->
											<span {...props} class="decoration-muted-foreground/50 font-medium underline decoration-dotted underline-offset-2">
												{compareChangeText}
											</span>
										{/snippet}
									</Tooltip.Trigger>
									<Tooltip.Content>
										<span class="whitespace-pre-line">{compareRangeDetail}</span>
									</Tooltip.Content>
								</Tooltip.Root>
							</Tooltip.Provider>
						{:else if compareUnavailable}
							<span aria-hidden="true">·</span>
							<span>Switch to Mean or Max to compare with previous period</span>
						{/if}
					</div>
				{/if}
			</div>
			<div class="flex shrink-0 items-center gap-3">
				{#if isHistogram && explorer.series.length > 1}
					<Select.Root
						type="single"
						value={String(histogramSeriesIndex)}
						onValueChange={(v) => v && (histogramSeriesIndex = Number(v))}
					>
						<!-- Select.Trigger's own base classes are `whitespace-nowrap` with no
						     truncation for raw text children (its `line-clamp-1` rule only
						     targets a `data-slot="select-value"` child, which plain text isn't) -
						     a full seriesLabel() (every attribute, unbounded) inside a fixed
						     w-56 trigger just overflowed past its own edges into the header's
						     other content instead of clipping. min-w-0 lets this flex child
						     actually shrink below its content size (the usual flexbox
						     truncation gotcha) so `truncate` has room to take effect; `title`
						     carries the full label for whoever needs it on hover. -->
						{@const label = seriesLabel(explorer.series[histogramSeriesIndex])}
						<Select.Trigger class="w-56 shrink-0">
							<span class="min-w-0 truncate" title={label}>{label}</span>
						</Select.Trigger>
						<Select.Content>
							{#each explorer.series as series, i (series.serviceName + JSON.stringify(series.attributes))}
								<Select.Item value={String(i)} label={seriesLabel(series)} />
							{/each}
						</Select.Content>
					</Select.Root>
				{/if}
				{#if logsHref}
					<Button variant="link" size="sm" href={logsHref} class="h-auto p-0">View related logs →</Button>
				{/if}
				{#if tracesHref}
					<Button variant="link" size="sm" href={tracesHref} class="h-auto p-0">View traces →</Button>
				{/if}
			</div>
		</div>

		<div class="min-h-0 flex-1 overflow-auto px-4 py-3">
			<!-- Wraps the *whole* loading/error/empty/chart decision below, not just the
			     chart branch - a metric switch (unlike toggling compare) can't stale-while-
			     revalidate (MetricsExplorerState.#resetForNewMetric clears immediately, a
			     different metric's chart would be actively misleading to leave up - see its
			     own remarks), so it genuinely passes through the loading branch. Keying on
			     chartKey (metric + service + compareActive, unchanged by a plain data
			     refresh) rather than a broader key covering every one of the four branches
			     individually: the fade-out of the *old* metric's chart and the fade-in of
			     the *new* metric's spinner both play as one transition when chartKey
			     changes; once data for that same target arrives, the spinner-to-chart swap
			     underneath is instant (no second fade queued up mid-transition) - avoids
			     stacking multiple fade cycles for a query that resolves before the first one
			     even finishes, which is the common case against local ClickHouse. -->
			<div use:animateHeight class="overflow-hidden">
			{#key chartKey}
			<!-- Sequential, not overlapping: `in:` is delayed by exactly `out:`'s duration
			     (FADE_MS, declared above) so the old view fully fades to nothing before the new
			     one starts fading in, rather than both fading through each other at once -
			     a plain shared `transition:fade` runs its outro/intro concurrently, which
			     read as a messier crossfade than a clean "gone, then here" swap once the
			     content on either side is a genuinely different shape (comparison mode's
			     whole point, and a metric switch's too). Slow enough (500ms total) to read
			     as a deliberate transition, not a blip - animateHeight's own duration
			     matches so the container's resize keeps pace with it instead of finishing
			     early. -->
			<div out:fade={{ duration: FADE_MS }} in:fade={{ duration: FADE_MS, delay: FADE_MS }}>
			{#if explorer.queryLoading && lines.length === 0}
				<!-- h-[180px], not h-full: h-full only resolves against an ancestor with an
				     *explicit* height, and the two wrappers this now sits inside
				     (use:animateHeight, out:fade/in:fade) are deliberately auto-height (that's
				     the whole point of animateHeight - size to content) - h-full would silently
				     collapse to the Spinner's own tiny intrinsic size instead of filling the
				     body. A fixed height sidesteps that, and matches "No data in range"'s own
				     h-[180px] below, so loading/empty don't jump size against each other either. -->
				<div class="flex h-[180px] items-center justify-center">
					<Spinner />
				</div>
			{:else if explorer.queryError}
				<p class="text-destructive text-xs">{explorer.queryError}</p>
			{:else if bucketTimes.length === 0}
				<div class="text-muted-foreground flex h-[180px] items-center justify-center text-xs">No data in range</div>
			{:else}
				<div class="flex gap-2">
					<!-- Rendered as real DOM text, not SVG <text>, deliberately: the chart's
					     viewBox is stretched non-uniformly (preserveAspectRatio="none", see
					     below) to fill whatever width the container has, which would otherwise
					     stretch glyph shapes horizontally by that same ratio. Positioned via
					     the identical yFor() used for the SVG gridlines below, so labels stay
					     pixel-aligned with them despite living outside the SVG.
					     w-28, not the tighter width this used to have - a rate unit built from a
					     UCUM curly-brace annotation (axis.ts's own remarks: "{collection}/s" ->
					     "collection/s") spells out the counted noun in full, easily 10+
					     characters wider than "ms"/"MB"/"%". Was clipping *silently* before
					     (an absolutely-positioned, unconstrained-width span just overflows past
					     its container's edge with nothing to stop it) rather than truncating
					     visibly - inset-x-1 (not just right-1) below gives each span an actual
					     bounded width so `truncate` has something to truncate against, with the
					     full text still available via `title` for whatever's still too long to
					     fit even at this width. -->
					<div
						class="text-muted-foreground relative w-28 shrink-0 text-right text-xs whitespace-nowrap tabular-nums"
						style="height: {CHART_HEIGHT}px"
					>
						{#each ticks.values as tick (tick)}
							{@const label = formatAtScale(tick, axisScale)}
							<span class="absolute inset-x-1 -translate-y-1/2 truncate leading-none" style="top: {yFor(tick)}px" title={label}>
								{label}
							</span>
						{/each}
					</div>
					<Tooltip.Provider>
						<Tooltip.Root open={hoverIndex !== null}>
							<Tooltip.Trigger>
								{#snippet child({ props })}
									<svg
										{...props}
										viewBox="0 0 {CHART_WIDTH} {CHART_HEIGHT}"
										preserveAspectRatio="none"
										class="h-[180px] w-full min-w-0"
										role="img"
										aria-label="{explorer.selected!.metricName} over time"
										onpointermove={handlePointerMove}
										onpointerleave={() => (hoverIndex = null)}
									>
										{#each ticks.values as tick (tick)}
											<line
												x1="0"
												y1={yFor(tick)}
												x2={CHART_WIDTH}
												y2={yFor(tick)}
												class="text-border"
												stroke="currentColor"
												stroke-width="1"
												vector-effect="non-scaling-stroke"
											/>
										{/each}

										{#if hoverIndex !== null}
											<line
												x1={xFor(bucketTimes[hoverIndex])}
												y1={PEAK_Y}
												x2={xFor(bucketTimes[hoverIndex])}
												y2={BASELINE_Y}
												class="text-muted-foreground"
												stroke="currentColor"
												stroke-width="1"
												stroke-dasharray="2,2"
												vector-effect="non-scaling-stroke"
											/>
										{/if}

										{#each lines as line (line.label)}
											<path
												d={pathFor(line.points)}
												fill="none"
												stroke={line.color}
												stroke-width="2"
												stroke-linecap="round"
												stroke-linejoin="round"
												stroke-dasharray={line.dashed ? '5,4' : undefined}
												vector-effect="non-scaling-stroke"
											/>
											{#each line.points as point (point.time)}
												<circle
													cx={xFor(point.time)}
													cy={yFor(point.raw)}
													r={bucketTimes.length > 60 ? 0 : 2.5}
													fill={line.color}
												/>
											{/each}
										{/each}
									</svg>
								{/snippet}
							</Tooltip.Trigger>
							{#if hoverIndex !== null}
								<Tooltip.Content>
									<div class="flex flex-col gap-0.5">
										<span class="font-medium">{formatBucketTime(bucketTimes[hoverIndex])}</span>
										{#each lines as line (line.label)}
											{@const point = pointAtHover(line)}
											{#if point}
												<span class="flex items-center gap-1.5">
													<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"></span>
													{line.detail}: {formatValue(point.raw)}
												</span>
											{/if}
										{/each}
									</div>
								</Tooltip.Content>
							{/if}
						</Tooltip.Root>
					</Tooltip.Provider>
				</div>

				{#if lines.length > 1}
					<Tooltip.Provider>
						<div class="text-muted-foreground mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs">
							{#each lines as line (line.label)}
								{#if line.detail === line.label}
									<span class="flex items-center gap-1.5">
										<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"></span>
										{line.label}
									</span>
								{:else}
									<!-- compactSeriesLabel hid attributes shared across the visible set
									     to keep this row readable with many series (e.g. one exception
									     type each) - the full dimension set is one hover away, not lost. -->
									<Tooltip.Root>
										<Tooltip.Trigger>
											{#snippet child({ props })}
												<span {...props} class="flex items-center gap-1.5">
													<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"
													></span>
													{line.label}
												</span>
											{/snippet}
										</Tooltip.Trigger>
										<Tooltip.Content>{line.detail}</Tooltip.Content>
									</Tooltip.Root>
								{/if}
							{/each}
						</div>
					</Tooltip.Provider>
				{/if}

				{#if hiddenSeriesCount > 0}
					<p class="text-muted-foreground mt-2 text-xs">
						+{hiddenSeriesCount} more series not shown ({MAX_SERIES} max) - narrow the service/attribute filter to see them.
					</p>
				{/if}
			{/if}
			</div>
			{/key}
			</div>
		</div>
	{/if}
</div>
