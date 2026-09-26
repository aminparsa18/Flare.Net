<script lang="ts">
	// Value-distribution histogram: X is the value range, Y how many bucket readings fell in
	// it (see `histogramBins` for the binning rule). Every series is pooled into one
	// distribution - the question this answers is "what values does this query usually take",
	// which per-series colouring on overlapping bins would only muddy. Same hand-rolled
	// viewBox-SVG + pinned-open Tooltip technique as BarVisualization.svelte.
	//
	// Thresholds are value rules, and values are the X axis here, so a bin takes the color of
	// the first rule matching its midpoint rather than drawing a horizontal line - "red above
	// 500 ms" turns the slow tail red.
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { formatAtScale, niceAxisTicks, resolveAxisScale } from '$lib/metrics/axis';
	import { SERIES_COLOR_VARS } from '$lib/metrics/chart-colors';
	import { matchThreshold, thresholdColorValue, type PanelThreshold } from '$lib/dashboards/thresholds';
	import { histogramBins, type VizSeries } from '$lib/dashboards/visualization';
	import * as m from '$lib/paraglide/messages';

	let {
		series,
		unit,
		thresholds = []
	}: {
		series: VizSeries[];
		unit: string | null;
		thresholds?: PanelThreshold[];
	} = $props();

	const readings = $derived(series.flatMap((s) => s.points.map((p) => p.value)));
	const histogram = $derived(histogramBins(readings, unit));
	const bins = $derived(histogram.bins);

	const peakCount = $derived(Math.max(1, ...bins.map((b) => b.count)));
	// Counts are plain numbers - no unit - so the Y axis uses the dimensionless scale.
	const countScale = resolveAxisScale(null, 0);
	const ticks = $derived(niceAxisTicks(0, peakCount, countScale));
	const maxValue = $derived(Math.max(1, ticks.max));

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 180;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	function yFor(count: number): number {
		return BASELINE_Y - (count / maxValue) * (BASELINE_Y - PEAK_Y);
	}

	const slotWidth = $derived(bins.length > 0 ? CHART_WIDTH / bins.length : CHART_WIDTH);
	const GAP = 2;

	function binColor(from: number, to: number): string {
		const match = matchThreshold(thresholds, (from + to) / 2);
		return match ? thresholdColorValue(match.color) : `var(${SERIES_COLOR_VARS[0]})`;
	}

	function binLabel(from: number, to: number): string {
		return from === to ? formatAtScale(from, histogram.scale) : `${formatAtScale(from, histogram.scale)} – ${formatAtScale(to, histogram.scale)}`;
	}

	/** Label every edge only while they fit; past ~10 bins, every other one. */
	const labelEvery = $derived(bins.length > 10 ? 2 : 1);

	let hoverIndex = $state<number | null>(null);
	const safeHoverIndex = $derived(hoverIndex !== null && hoverIndex < bins.length ? hoverIndex : null);

	function handlePointerMove(e: PointerEvent) {
		if (bins.length === 0) return;
		const rect = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
		const fraction = (e.clientX - rect.left) / rect.width;
		hoverIndex = Math.min(bins.length - 1, Math.max(0, Math.floor(fraction * bins.length)));
	}
</script>

<div class="flex min-w-0 flex-1 flex-col">
	<div class="relative">
		<div class="text-muted-foreground pointer-events-none absolute inset-y-0 left-0 w-10 text-[10px]" style="height: {CHART_HEIGHT}px">
			{#each ticks.values as tick (tick)}
				{@const label = formatAtScale(tick, countScale)}
				<span class="absolute inset-x-1 -translate-y-1/2 truncate leading-none" style="top: {yFor(tick)}px" title={label}>{label}</span>
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
							aria-label={m.panelVisualization_histogramAriaLabel()}
							onpointermove={handlePointerMove}
							onpointerleave={() => (hoverIndex = null)}
						>
							{#each ticks.values as tick (tick)}
								<line x1="0" y1={yFor(tick)} x2={CHART_WIDTH} y2={yFor(tick)} class="text-border" stroke="currentColor" stroke-width="1" vector-effect="non-scaling-stroke" />
							{/each}
							{#if safeHoverIndex !== null}
								<rect x={safeHoverIndex * slotWidth} y={PEAK_Y} width={slotWidth} height={BASELINE_Y - PEAK_Y} class="text-muted" fill="currentColor" opacity="0.5" />
							{/if}
							{#each bins as bin, i (i)}
								{#if bin.count > 0}
									<rect x={i * slotWidth + GAP / 2} y={yFor(bin.count)} width={Math.max(1, slotWidth - GAP)} height={BASELINE_Y - yFor(bin.count)} fill={binColor(bin.from, bin.to)} />
								{/if}
							{/each}
						</svg>
					{/snippet}
				</Tooltip.Trigger>
				{#if safeHoverIndex !== null}
					{@const bin = bins[safeHoverIndex]}
					<Tooltip.Content>
						<div class="flex flex-col gap-0.5">
							<span class="font-medium">{binLabel(bin.from, bin.to)}</span>
							<span>{m.panelVisualization_histogramCount({ count: bin.count })}</span>
						</div>
					</Tooltip.Content>
				{/if}
			</Tooltip.Root>
		</Tooltip.Provider>
	</div>

	<div class="text-muted-foreground relative mt-1 ml-10 h-3 text-[10px]">
		{#each bins as bin, i (i)}
			{#if i % labelEvery === 0}
				<span class="absolute leading-none whitespace-nowrap {i === 0 ? '' : '-translate-x-1/2'}" style="left: {(i / bins.length) * 100}%">{formatAtScale(bin.from, histogram.scale)}</span>
			{/if}
		{/each}
		{#if bins.length > 0 && bins.length % labelEvery === 0}
			<span class="absolute -translate-x-full leading-none whitespace-nowrap" style="left: 100%">{formatAtScale(bins[bins.length - 1].to, histogram.scale)}</span>
		{/if}
	</div>
	<p class="text-muted-foreground mt-1 text-xs">{m.panelVisualization_histogramReadings({ count: readings.length })}</p>
</div>
