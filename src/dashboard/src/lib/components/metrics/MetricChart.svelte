<script lang="ts">
	// Hand-rolled multi-line chart - no charting library, same "hand-roll it" precedent
	// VolumeChart/TraceWaterfall already set. Reuses VolumeChart's core technique
	// wholesale (viewBox SVG, array-index x positions rather than a real time scale,
	// pointer-move hover mapped to the nearest index, Tooltip.Root pinned open by
	// hover state) - the genuinely new part is multiple simultaneous lines instead of
	// one bar series, so see this file's own remarks for what that changes.
	import * as Tooltip from '$lib/components/ui/tooltip';
	import * as Select from '$lib/components/ui/select';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Button } from '$lib/components/ui/button';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import { formatAtScale, niceAxisTicks, resolveAxisScale } from '$lib/metrics/axis';
	import { formatBucketWidthSeconds } from '$lib/logs/bucket-width';
	import { buildLogsDeepLinkHref, buildTracesDeepLinkHref } from '$lib/deep-links';
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
	const PERCENTILE_COLOR: Record<'p50' | 'p90' | 'p99', string> = {
		p50: 'var(--chart-1)',
		p90: 'var(--chart-2)',
		p99: 'var(--chart-4)'
	};
	// Its own fixed slot (unused by PERCENTILE_COLOR above) - mean isn't a percentile,
	// so it shouldn't borrow p50's color and read as if it were one.
	const MEAN_COLOR = 'var(--chart-3)';

	// Alternate views for Sum/Histogram, picked via the small Select next to the
	// metadata row below - "how should this be aggregated", not "which series". Both
	// are pure reshapes of the *already-fetched* data (rate = value / bucket width;
	// mean = sum / count, both already returned per point - see MetricSeriesPoint),
	// so switching never re-queries. `count`/`p75`/`p95`/`max` are deliberately not
	// options here - see Planning.md's "Later" entry for why those need backend work
	// this Tier-1 pass doesn't do.
	// Rate, not Sum, is the default view for Sum-typed metrics: bucket width here
	// tracks the selected time range (see intervalSeconds' own remarks), so a raw
	// per-bucket Sum for the same underlying counter reads differently depending on
	// which range happens to be selected - "100 exceptions" means something
	// different over 10s than over 1h. Dividing by bucket width makes the number
	// mean the same thing regardless of zoom level, which is what "exceptions/min"
	// operationally answers anyway. Sum stays one click away in the picker below.
	type SumMode = 'sum' | 'rate';
	type HistogramMode = 'percentiles' | 'mean';
	let sumMode = $state<SumMode>('rate');
	let histogramMode = $state<HistogramMode>('percentiles');

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
	// carries. Real "group by a chosen attribute" (collapsing series server-side)
	// is a bigger, separate piece of work - see Planning.md's Later item - this
	// is just hiding what's already redundant within the visible set.
	function compactSeriesLabel(series: MetricSeries, visible: MetricSeries[]): string {
		if (visible.length <= 1) return seriesLabel(series);

		const serviceNameVaries = new Set(visible.map((s) => s.serviceName)).size > 1;
		const attrKeys = [...new Set(visible.flatMap((s) => Object.keys(s.attributes)))];
		const varyingKeys = attrKeys.filter((k) => new Set(visible.map((s) => s.attributes[k] ?? '')).size > 1);

		const parts: string[] = [];
		if (serviceNameVaries) parts.push(series.serviceName);
		if (varyingKeys.length === 1) {
			// Sole distinguishing dimension - the bare value ("InvalidOperationException")
			// reads better than the key-prefixed form repeated on every line.
			parts.push(series.attributes[varyingKeys[0]] ?? '(none)');
		} else {
			parts.push(...varyingKeys.map((k) => `${k}=${series.attributes[k] ?? '(none)'}`));
		}
		// Nothing varies (shouldn't happen - the backend groups series by distinct
		// attribute combinations, see MetricSeriesQueryBuilder's remarks) - fall
		// back to the full label rather than an empty legend entry.
		return parts.length > 0 ? parts.join(' · ') : seriesLabel(series);
	}

	const isHistogram = $derived(explorer.selected?.type === 'Histogram');
	const isSum = $derived(explorer.selected?.type === 'Sum');

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
	const hiddenSeriesCount = $derived(isHistogram ? 0 : Math.max(0, explorer.series.length - MAX_SERIES));

	interface PlotPoint {
		bucketStart: string;
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
		points: PlotPoint[];
	}

	function buildLines(): LineSpec[] {
		if (isHistogram) {
			const series = visibleSeries[0];
			if (!series) return [];
			if (histogramMode === 'mean') {
				return [
					{
						color: MEAN_COLOR,
						label: 'mean',
						detail: 'mean',
						points: series.points
							.filter((p) => p.sum != null && p.count != null && p.count > 0)
							.map((p) => ({ bucketStart: p.bucketStart, raw: p.sum! / p.count! }))
					}
				];
			}
			return (['p50', 'p90', 'p99'] as const).map((key) => ({
				color: PERCENTILE_COLOR[key],
				label: key,
				detail: key,
				points: series.points.filter((p) => p[key] != null).map((p) => ({ bucketStart: p.bucketStart, raw: p[key]! }))
			}));
		}

		// Rate divides the already-returned per-bucket value by that bucket's width -
		// no re-query, since every bucket in one response shares the same width
		// (bucketWidthSeconds is one request-level parameter, not per-bucket).
		const rateDivisor = isSum && sumMode === 'rate' ? explorer.intervalSeconds : null;
		return visibleSeries.map((series, i) => ({
			color: `var(${SERIES_COLOR_VARS[i]})`,
			label: compactSeriesLabel(series, visibleSeries),
			detail: seriesLabel(series),
			points: series.points
				.filter((p) => p.value != null)
				.map((p) => ({ bucketStart: p.bucketStart, raw: rateDivisor ? p.value! / rateDivisor : p.value! }))
		}));
	}

	const lines = $derived(buildLines());

	// Shared x-domain: every distinct bucket across the visible lines, in order - same
	// "array-index position, not a real time scale" simplification VolumeChart already
	// uses (see its own remarks). Known v1 simplification: a line missing a bucket
	// (no data point at that index) draws straight through to its next point rather
	// than breaking - acceptable for the sparse-gap case this is meant to handle, not
	// meant to imply interpolated data across a large hole.
	const bucketStarts = $derived([...new Set(lines.flatMap((l) => l.points.map((p) => p.bucketStart)))].sort());
	const bucketIndexOf = $derived(new Map(bucketStarts.map((b, i) => [b, i])));

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 180;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	const peakValue = $derived(Math.max(0, ...lines.flatMap((l) => l.points.map((p) => p.raw))));

	// Rate mode changes the unit, not just the numbers - a Sum declared "By" reads as
	// "By/s" once every value has been divided by the bucket width. axis.ts already
	// knows how to split/format a "<unit>/s" rate unit (composing it back this way is
	// simplest: MetricNameInfo has no separate "rate unit" of its own to read).
	const displayUnit = $derived(
		isSum && sumMode === 'rate' ? `${explorer.selected?.unit ?? ''}/s` : explorer.selected?.unit
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

	function xFor(bucketStart: string): number {
		const count = bucketStarts.length;
		if (count <= 1) return CHART_WIDTH / 2;
		return (bucketIndexOf.get(bucketStart)! / (count - 1)) * CHART_WIDTH;
	}

	function yFor(raw: number): number {
		return BASELINE_Y - (raw / maxValue) * (BASELINE_Y - PEAK_Y);
	}

	function pathFor(points: PlotPoint[]): string {
		return points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${xFor(p.bucketStart)} ${yFor(p.raw)}`).join(' ');
	}

	let hoverIndex = $state<number | null>(null);

	function handlePointerMove(e: PointerEvent) {
		if (bucketStarts.length === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		const rect = svg.getBoundingClientRect();
		const fraction = (e.clientX - rect.left) / rect.width;
		hoverIndex = Math.min(bucketStarts.length - 1, Math.max(0, Math.round(fraction * (bucketStarts.length - 1))));
	}

	function pointAtHover(line: LineSpec): PlotPoint | undefined {
		if (hoverIndex === null) return undefined;
		return line.points.find((p) => p.bucketStart === bucketStarts[hoverIndex!]);
	}

	function formatBucketTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, {
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
									{sumMode === 'rate' ? 'Rate' : 'Sum'}
								</Select.Trigger>
								<Select.Content>
									<Select.Item value="sum" label="Sum" />
									<Select.Item value="rate" label="Rate" />
								</Select.Content>
							</Select.Root>
						{:else if isHistogram}
							<Select.Root
								type="single"
								value={histogramMode}
								onValueChange={(v) => v && (histogramMode = v as HistogramMode)}
							>
								<Select.Trigger class="w-auto">
									{histogramMode === 'mean' ? 'Mean' : 'Percentiles'}
								</Select.Trigger>
								<Select.Content>
									<Select.Item value="percentiles" label="Percentiles" />
									<Select.Item value="mean" label="Mean" />
								</Select.Content>
							</Select.Root>
						{:else}
							<span>{explorer.selected.type}</span>
						{/if}
						<span aria-hidden="true">·</span>
						<span>{explorer.series.length} series</span>
						<span aria-hidden="true">·</span>
						<span>{formatBucketWidthSeconds(explorer.intervalSeconds)} interval</span>
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
						<Select.Trigger class="w-56 shrink-0">
							{seriesLabel(explorer.series[histogramSeriesIndex])}
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
			{#if explorer.queryLoading && lines.length === 0}
				<div class="flex h-full items-center justify-center">
					<Spinner />
				</div>
			{:else if explorer.queryError}
				<p class="text-destructive text-xs">{explorer.queryError}</p>
			{:else if bucketStarts.length === 0}
				<div class="text-muted-foreground flex h-[180px] items-center justify-center text-xs">No data in range</div>
			{:else}
				<div class="flex gap-2">
					<!-- Rendered as real DOM text, not SVG <text>, deliberately: the chart's
					     viewBox is stretched non-uniformly (preserveAspectRatio="none", see
					     below) to fill whatever width the container has, which would otherwise
					     stretch glyph shapes horizontally by that same ratio. Positioned via
					     the identical yFor() used for the SVG gridlines below, so labels stay
					     pixel-aligned with them despite living outside the SVG. -->
					<div
						class="text-muted-foreground relative w-14 shrink-0 text-right text-xs whitespace-nowrap tabular-nums"
						style="height: {CHART_HEIGHT}px"
					>
						{#each ticks.values as tick (tick)}
							<span class="absolute right-1 -translate-y-1/2 leading-none" style="top: {yFor(tick)}px">
								{formatAtScale(tick, axisScale)}
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
												x1={xFor(bucketStarts[hoverIndex])}
												y1={PEAK_Y}
												x2={xFor(bucketStarts[hoverIndex])}
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
												vector-effect="non-scaling-stroke"
											/>
											{#each line.points as point (point.bucketStart)}
												<circle
													cx={xFor(point.bucketStart)}
													cy={yFor(point.raw)}
													r={bucketStarts.length > 60 ? 0 : 2.5}
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
										<span class="font-medium">{formatBucketTime(bucketStarts[hoverIndex])}</span>
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
	{/if}
</div>
