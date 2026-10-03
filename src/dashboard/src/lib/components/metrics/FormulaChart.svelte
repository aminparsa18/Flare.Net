<script lang="ts">
	// Formula mode's chart - deliberately a separate, much smaller component from
	// MetricChart.svelte rather than teaching that 1000+-line file a second mode: a formula
	// result has none of the things MetricChart is built around (one selected metric's name/
	// unit/type, Sum rate/count modes, Histogram percentile modes, comparison-period lines) -
	// just N joined (serviceName, attributes) series, each a plain Value per bucket (see
	// MetricsExplorerState.mode's own remarks). Reuses the same hand-rolled-SVG technique and
	// axis.ts utilities MetricChart/VolumeChart already use, at a fraction of the branching.
	//
	// No drag-to-zoom, no per-series 5-color cap/hidden-series note, no unit scale beyond
	// "dimensionless" - v1 scope cuts (docs-internal/adr/0036-cross-query-metric-formulas.md),
	// not omissions; MetricChart's own equivalents stay the place to look for that richer
	// feature set if/when Formula mode grows into it.
	import * as Tooltip from '$lib/components/ui/tooltip';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import { metricsExplorerContext } from '$lib/metrics/context';
	import { axisFraction, formatAtScale, formatAutoScaled, logAxisTicks, niceAxisTicks, resolveAxisScale, type YAxisScale } from '$lib/metrics/axis';
	import BucketIntervalMenu from '$lib/components/logs/BucketIntervalMenu.svelte';
	import { seriesColor } from '$lib/metrics/chart-colors';
	import { seriesLabel } from '$lib/dashboards/visualization';
	import { areaPath, stackExtent, stackLines, topEdgePath } from '$lib/dashboards/stacking';
	import type { PanelStacking } from '$lib/dashboards/visualization';
	import ThresholdOverlay from './ThresholdOverlay.svelte';
	import YAxisScaleToggle from './YAxisScaleToggle.svelte';
	import ChartCsvButton from './ChartCsvButton.svelte';
	import { downloadTimeSeriesCsv } from '$lib/dashboards/chart-csv';
	import { matchThreshold, thresholdColorValue, type PanelThreshold, type ThresholdColor } from '$lib/dashboards/thresholds';
	import { formatSeriesLabel, legendLayout, seriesColorOverride, type LegendPosition } from '$lib/dashboards/legend';
	import * as m from '$lib/paraglide/messages';
	import { formatChartTime } from '$lib/time/format';

	// yAxisMin/yAxisMax: same soft Y-axis floor/ceiling MetricChart.svelte's own props of
	// the same name apply (DashboardPanel.yAxisMin/yAxisMax) - only ever set from a
	// dashboard panel, never from the Explorer page itself, which never passes them.
	// `thresholds`, `yAxisScale`, `legendPosition` and `seriesColors` likewise mirror
	// MetricChart's own props of the same name.
	let {
		yAxisMin = null,
		yAxisMax = null,
		decimals,
		thresholds = [],
		yAxisScale,
		legendPosition = 'bottom',
		seriesColors = {},
		legendFormat,
		stacking = 'none',
		title = ''
	}: {
		yAxisMin?: number | null;
		yAxisMax?: number | null;
		/** `DashboardPanel.decimals` - fixed fraction digits; `undefined` is auto. */
		decimals?: number;
		thresholds?: PanelThreshold[];
		yAxisScale?: YAxisScale;
		legendPosition?: LegendPosition;
		seriesColors?: Record<string, ThresholdColor>;
		legendFormat?: string;
		/** Stacked areas instead of lines (`DashboardPanel.stacking`); `percent` fills every bucket to 0-100%. */
		stacking?: PanelStacking;
		/** The panel's title - names the downloaded CSV; the formula itself on the Explorer page. */
		title?: string;
	} = $props();

	const explorer = metricsExplorerContext.get();

	interface LineSpec {
		label: string;
		color: string;
		points: { time: number; raw: number }[];
	}

	const lines = $derived<LineSpec[]>(
		explorer.formulaSeries.map((series) => ({
			label: formatSeriesLabel(legendFormat, series) ?? seriesLabel(series),
			color: seriesColorOverride(seriesColors, seriesLabel(series)) ?? seriesColor(seriesLabel(series)),
			points: series.points.filter((p): p is typeof p & { value: number } => p.value != null).map((p) => ({ time: new Date(p.bucketStart).getTime(), raw: p.value }))
		}))
	);

	const layout = $derived(legendLayout(legendPosition === 'right' ? 'right' : 'bottom'));

	const bucketTimes = $derived([...new Set(lines.flatMap((l) => l.points.map((p) => p.time)))].sort((a, b) => a - b));
	const bucketIndexOf = $derived(new Map(bucketTimes.map((t, i) => [t, i])));

	// Stacked areas (ADR-0086): per-series bands instead of lines. Log scale can't stack, so
	// stacking wins over it; percent mode also drops the soft Y bounds and thresholds, which
	// are in the formula's own units.
	const stackActive = $derived(stacking !== 'none' && lines.length > 0);
	const percent = $derived(stackActive && stacking === 'percent');
	const layers = $derived(stackActive ? stackLines(lines, bucketTimes, stacking as 'normal' | 'percent') : []);
	const extent = $derived(stackExtent(layers));

	const rawValues = $derived(lines.flatMap((l) => l.points.map((p) => p.raw)));
	const dataMax = $derived(stackActive ? extent.hi : rawValues.length > 0 ? Math.max(...rawValues) : 0);
	const dataMin = $derived(stackActive ? extent.lo : rawValues.length > 0 ? Math.min(0, ...rawValues) : 0);

	// yAxisMin/yAxisMax narrow the "nice" domain the same way MetricChart's own
	// domainMin/domainMax do - see that file's remarks for how "soft" is applied (the bound
	// only ever widens the domain outward, never clips data that falls past it).
	const domainMin = $derived(yAxisMin != null && !percent ? Math.min(yAxisMin, dataMin) : dataMin);
	const domainMax = $derived(yAxisMax != null && !percent ? Math.max(yAxisMax, dataMax) : dataMax);

	// Dimensionless - a formula result has no single declared unit of its own (its operands
	// might each have different, or no, units - e.g. `(A/B)*100` for a ratio-as-percentage
	// has no meaningful inherited unit either), so this always resolves the "no unit" branch.
	const axisScale = $derived(resolveAxisScale(null, Math.max(Math.abs(domainMin), Math.abs(domainMax))));
	// Log scale - same decade ticks and "positive values only, else stay linear" fallback as
	// MetricChart's own `logActive`/`ticks`; see there.
	const positiveValues = $derived(rawValues.filter((v) => v > 0));
	const logActive = $derived(!stackActive && (yAxisScale ?? explorer.filter.yAxisScale) === 'log' && positiveValues.length > 0);
	const ticks = $derived.by(() => {
		if (!logActive) return niceAxisTicks(domainMin, domainMax, axisScale);
		const lo = Math.min(...positiveValues, yAxisMin != null && yAxisMin > 0 ? yAxisMin : Infinity);
		const hi = Math.max(dataMax, yAxisMax != null && yAxisMax > 0 ? yAxisMax : 0);
		return logAxisTicks(lo, hi, null);
	});
	const minValue = $derived(ticks.min);
	const maxValue = $derived(Math.max(minValue + 1e-9, ticks.max));

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 180;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	function xFor(time: number): number {
		const count = bucketTimes.length;
		if (count <= 1) return CHART_WIDTH / 2;
		return (bucketIndexOf.get(time)! / (count - 1)) * CHART_WIDTH;
	}

	function yFor(raw: number): number {
		return BASELINE_Y - (axisFraction(raw, minValue, maxValue, logActive) ?? 0) * (BASELINE_Y - PEAK_Y);
	}

	function plotted(points: LineSpec['points']): LineSpec['points'] {
		return logActive ? points.filter((p) => p.raw > 0) : points;
	}

	// Breaks the line at a point log can't place - see MetricChart's own pathFor.
	function pathFor(points: LineSpec['points']): string {
		let d = '';
		let pen = 'M';
		for (const p of points) {
			if (logActive && !(p.raw > 0)) {
				pen = 'M';
				continue;
			}
			d += `${pen} ${xFor(p.time)} ${yFor(p.raw)} `;
			pen = 'L';
		}
		return d.trimEnd();
	}

	let hoverIndex = $state<number | null>(null);
	const safeHoverIndex = $derived(hoverIndex !== null && hoverIndex < bucketTimes.length ? hoverIndex : null);

	function pointAtHover(line: LineSpec) {
		if (safeHoverIndex === null) return undefined;
		return line.points.find((p) => p.time === bucketTimes[safeHoverIndex]);
	}

	function handlePointerMove(e: PointerEvent) {
		if (bucketTimes.length === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		const rect = svg.getBoundingClientRect();
		const fraction = (e.clientX - rect.left) / rect.width;
		hoverIndex = Math.min(bucketTimes.length - 1, Math.max(0, Math.round(fraction * (bucketTimes.length - 1))));
	}

	function formatBucketTime(time: number): string {
		return formatChartTime(time);
	}

	function downloadCsv(): void {
		downloadTimeSeriesCsv(
			lines.map((l) => ({ label: l.label, points: l.points.map((p) => ({ time: p.time, value: p.raw })) })),
			title || explorer.formulaExpression
		);
	}

	/** Axis tick label - `50%` on a percent-stacked chart. */
	function formatTick(n: number): string {
		return percent ? `${formatAtScale(n, axisScale, 0)}%` : formatValue(n);
	}

	function layerAtHover(layer: (typeof layers)[number]) {
		if (safeHoverIndex === null) return undefined;
		const p = layer.points[safeHoverIndex];
		return p && p.raw != null ? p : undefined;
	}

	function formatValue(n: number): string {
		return logActive ? formatAutoScaled(n, null, decimals) : formatAtScale(n, axisScale, decimals);
	}
</script>

<div class="flex min-h-0 flex-1 flex-col overflow-y-auto p-4">
	<div class="mb-2 flex shrink-0 items-center justify-between gap-2">
		<h2 class="text-muted-foreground truncate font-mono text-sm">{explorer.formulaExpression}</h2>
		{#if explorer.formulaLoading}
			<Spinner class="size-3.5" />
		{/if}
	</div>

	{#if explorer.formulaIntervalSeconds !== null}
		<div class="text-muted-foreground mb-2 flex flex-wrap items-center gap-x-1.5 text-xs">
			<span>{m.metricChart_seriesCount({ count: explorer.formulaSeries.length })}</span>
			<span aria-hidden="true">·</span>
			<BucketIntervalMenu
				value={explorer.filter.bucketWidthSeconds}
				effectiveSeconds={explorer.formulaIntervalSeconds}
				rangeSeconds={explorer.formulaRangeFrom && explorer.formulaRangeTo ? (new Date(explorer.formulaRangeTo).getTime() - new Date(explorer.formulaRangeFrom).getTime()) / 1000 : null}
				onChange={(seconds) => explorer.setBucketWidthSeconds(seconds)}
			/>
			{#if lines.length > 0}
				<span aria-hidden="true">·</span>
				<ChartCsvButton onclick={downloadCsv} />
			{/if}
			{#if yAxisScale === undefined}
				<span aria-hidden="true">·</span>
				<YAxisScaleToggle value={explorer.filter.yAxisScale} onChange={(scale) => explorer.setYAxisScale(scale)} />
			{/if}
		</div>
	{/if}

	{#if explorer.formulaError}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>{m.formulaChart_errorTitle()}</Empty.Title>
				<Empty.Description>{explorer.formulaError}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else if lines.length === 0}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>{m.formulaChart_noDataTitle()}</Empty.Title>
				<Empty.Description>{explorer.formulaWarning ?? m.formulaChart_noDataDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div class="flex min-w-0 flex-1 {layout.wrapper}">
			<div class="relative flex-1 {layout.plot}">
				<div class="text-muted-foreground pointer-events-none absolute inset-y-0 left-0 w-10 text-[10px]" style="height: {CHART_HEIGHT}px">
					{#each ticks.values as tick (tick)}
						{@const label = formatTick(tick)}
						<span class="absolute inset-x-1 -translate-y-1/2 truncate leading-none" style="top: {yFor(tick)}px" title={label}>
							{label}
						</span>
					{/each}
				</div>
				<Tooltip.Provider>
					<Tooltip.Root open={safeHoverIndex !== null}>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<svg
									{...props}
									viewBox="0 0 {CHART_WIDTH} {CHART_HEIGHT}"
									preserveAspectRatio="none"
									class="h-[180px] w-full min-w-0 cursor-crosshair pl-10"
									role="img"
									aria-label={m.formulaChart_ariaLabel()}
									onpointermove={handlePointerMove}
									onpointerleave={() => (hoverIndex = null)}
								>
									{#each ticks.values as tick (tick)}
										<line x1="0" y1={yFor(tick)} x2={CHART_WIDTH} y2={yFor(tick)} class="text-border" stroke="currentColor" stroke-width="1" vector-effect="non-scaling-stroke" />
									{/each}

									<ThresholdOverlay thresholds={percent ? [] : thresholds} {yFor} {minValue} {maxValue} width={CHART_WIDTH} peakY={PEAK_Y} baselineY={BASELINE_Y} />

									{#if safeHoverIndex !== null}
										<line
											x1={xFor(bucketTimes[safeHoverIndex])}
											y1={PEAK_Y}
											x2={xFor(bucketTimes[safeHoverIndex])}
											y2={BASELINE_Y}
											class="text-muted-foreground"
											stroke="currentColor"
											stroke-width="1"
											stroke-dasharray="2,2"
											vector-effect="non-scaling-stroke"
										/>
									{/if}

									{#if stackActive}
										{#each layers as layer, i (layer.label)}
											<path d={areaPath(layer.points, xFor, yFor)} fill={lines[i].color} fill-opacity="0.55" stroke="none" />
											<path d={topEdgePath(layer.points, xFor, yFor)} fill="none" stroke={lines[i].color} stroke-width="1.5" stroke-linejoin="round" vector-effect="non-scaling-stroke" />
										{/each}
									{:else}
									{#each lines as line (line.label)}
										<path d={pathFor(line.points)} fill="none" stroke={line.color} stroke-width="2" stroke-linecap="round" stroke-linejoin="round" vector-effect="non-scaling-stroke" />
										{#each plotted(line.points) as point (point.time)}
											<circle cx={xFor(point.time)} cy={yFor(point.raw)} r={bucketTimes.length > 60 ? 0 : 2.5} fill={line.color} />
										{/each}
									{/each}
									{/if}
								</svg>
							{/snippet}
						</Tooltip.Trigger>
						{#if safeHoverIndex !== null}
							<Tooltip.Content>
								<div class="flex flex-col gap-0.5">
									<span class="font-medium">{formatBucketTime(bucketTimes[safeHoverIndex])}</span>
									{#if stackActive}
										{#each layers as layer, i (layer.label)}
											{@const point = layerAtHover(layer)}
											{#if point}
												<span class="flex items-center gap-1.5">
													<span class="inline-block h-2 w-2 shrink-0 rounded-sm" style="background: {lines[i].color};"></span>
													{layer.label}: {formatAtScale(point.raw!, resolveAxisScale(null, Math.abs(point.raw!)), decimals)}
													{#if percent}<span class="text-muted-foreground">({point.share.toFixed(1)}%)</span>{/if}
												</span>
											{/if}
										{/each}
									{:else}
									{#each lines as line (line.label)}
										{@const point = pointAtHover(line)}
										{#if point}
											{@const match = matchThreshold(thresholds, point.raw)}
											<span class="flex items-center gap-1.5">
												<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"></span>
												{line.label}:
												<span class={match ? 'font-semibold' : undefined} style={match ? `color: ${thresholdColorValue(match.color)};` : undefined}>
													{formatValue(point.raw)}
												</span>
											</span>
										{/if}
									{/each}
									{/if}
								</div>
							</Tooltip.Content>
						{/if}
					</Tooltip.Root>
				</Tooltip.Provider>
			</div>

			{#if lines.length > 1 && legendPosition !== 'hidden'}
				<div class="text-muted-foreground text-xs {layout.legend}">
					{#each lines as line (line.label)}
						<span class="flex min-w-0 items-center gap-1.5">
							<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"></span>
							<span class="truncate" title={line.label}>{line.label}</span>
						</span>
					{/each}
				</div>
			{/if}
		</div>
	{/if}
</div>
