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
	import { metricsExplorerContext } from '$lib/metrics/context';
	import { formatAtScale, niceAxisTicks, resolveAxisScale } from '$lib/metrics/axis';
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

	function seriesLabel(series: MetricSeries): string {
		const attrs = Object.entries(series.attributes);
		if (attrs.length === 0) return series.serviceName;
		return `${series.serviceName} · ${attrs.map(([k, v]) => `${k}=${v}`).join(', ')}`;
	}

	const isHistogram = $derived(explorer.selected?.type === 'Histogram');

	// Histogram: chart one series' distribution (p50/p90/p99) at a time, picked below
	// - N series x 3 percentile lines each would be unreadable, and "look at this
	// instrument's latency shape" is a per-series question anyway. Gauge/Sum instead
	// overlay every (capped) series as its own line - those are single comparable
	// values, which is the actual point of overlaying them.
	let histogramSeriesIndex = $state(0);

	$effect(() => {
		// Reset the histogram series picker whenever the tracked metric changes
		// underneath it - same "abort/reset on reselect" idiom SpanDetailSheet uses for
		// its own local, view-only state.
		void explorer.selected;
		histogramSeriesIndex = 0;
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
		label: string;
		points: PlotPoint[];
	}

	function buildLines(): LineSpec[] {
		if (isHistogram) {
			const series = visibleSeries[0];
			if (!series) return [];
			return (['p50', 'p90', 'p99'] as const).map((key) => ({
				color: PERCENTILE_COLOR[key],
				label: key,
				points: series.points.filter((p) => p[key] != null).map((p) => ({ bucketStart: p.bucketStart, raw: p[key]! }))
			}));
		}

		return visibleSeries.map((series, i) => ({
			color: `var(${SERIES_COLOR_VARS[i]})`,
			label: seriesLabel(series),
			points: series.points.filter((p) => p.value != null).map((p) => ({ bucketStart: p.bucketStart, raw: p.value! }))
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

	// One scale for the whole chart (e.g. "ms"), picked from the data's raw peak so
	// every tick/tooltip value reads in the same unit instead of each re-picking its
	// own ("40 ms" next to "0.03 s").
	const axisScale = $derived(resolveAxisScale(explorer.selected?.unit, peakValue));

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
			</div>
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
													{line.label}: {formatValue(point.raw)}
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
					<div class="text-muted-foreground mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs">
						{#each lines as line (line.label)}
							<span class="flex items-center gap-1.5">
								<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"></span>
								{line.label}
							</span>
						{/each}
					</div>
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
