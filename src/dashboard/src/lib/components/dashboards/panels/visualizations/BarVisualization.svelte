<script lang="ts">
	// Bar / stacked-bar visualization of a Metrics panel's series over time. Same hand-rolled
	// viewBox-SVG technique, axis.ts scaling, ThresholdOverlay and pinned-open Tooltip as
	// FormulaChart.svelte - bars instead of lines. Grouped bars sit side by side inside each
	// bucket's slot; stacked bars pile positives upward and negatives downward from zero, so
	// a stack's height is always the bucket's total magnitude in that direction.
	//
	// Bars always anchor at zero (a bar's length *is* its value - a non-zero floor would
	// misstate it), so a soft `yAxisMin` above zero can't raise the floor here, only a
	// negative one can lower it. Capped at the palette's 5 series like MetricChart.
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { niceAxisTicks, resolveAxisScale, formatAtScale } from '$lib/metrics/axis';
	import { SERIES_COLOR_VARS, seriesColor } from '$lib/metrics/chart-colors';
	import ThresholdOverlay from '$lib/components/metrics/ThresholdOverlay.svelte';
	import { matchThreshold, thresholdColorValue, type PanelThreshold } from '$lib/dashboards/thresholds';
	import { byMagnitude, type VizSeries } from '$lib/dashboards/visualization';
	import * as m from '$lib/paraglide/messages';

	let {
		series,
		stacked,
		unit,
		yAxisMin = null,
		yAxisMax = null,
		thresholds = []
	}: {
		series: VizSeries[];
		stacked: boolean;
		unit: string | null;
		yAxisMin?: number | null;
		yAxisMax?: number | null;
		thresholds?: PanelThreshold[];
	} = $props();

	const MAX_SERIES = SERIES_COLOR_VARS.length;
	const visible = $derived(byMagnitude(series).slice(0, MAX_SERIES));
	const hiddenCount = $derived(Math.max(0, series.length - MAX_SERIES));

	const bucketTimes = $derived([...new Set(visible.flatMap((s) => s.points.map((p) => p.time)))].sort((a, b) => a - b));

	/** `values[bucket][series]` - `null` where that series has no point in that bucket. */
	const values = $derived.by(() => {
		const lookups = visible.map((s) => new Map(s.points.map((p) => [p.time, p.value])));
		return bucketTimes.map((t) => lookups.map((l) => l.get(t) ?? null));
	});

	const extent = $derived.by(() => {
		let lo = 0;
		let hi = 0;
		for (const row of values) {
			if (stacked) {
				let pos = 0;
				let neg = 0;
				for (const v of row) {
					if (v == null) continue;
					if (v >= 0) pos += v;
					else neg += v;
				}
				hi = Math.max(hi, pos);
				lo = Math.min(lo, neg);
			} else {
				for (const v of row) {
					if (v == null) continue;
					hi = Math.max(hi, v);
					lo = Math.min(lo, v);
				}
			}
		}
		return { lo, hi };
	});

	const domainMin = $derived(yAxisMin != null ? Math.min(yAxisMin, extent.lo) : extent.lo);
	const domainMax = $derived(yAxisMax != null ? Math.max(yAxisMax, extent.hi) : extent.hi);
	const axisScale = $derived(resolveAxisScale(unit, Math.max(Math.abs(domainMin), Math.abs(domainMax))));
	const ticks = $derived(niceAxisTicks(domainMin, domainMax, axisScale));
	const minValue = $derived(Math.min(0, ticks.min));
	const maxValue = $derived(Math.max(minValue + 1e-9, ticks.max));

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 180;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	function yFor(raw: number): number {
		return BASELINE_Y - ((raw - minValue) / (maxValue - minValue)) * (BASELINE_Y - PEAK_Y);
	}

	const slotWidth = $derived(bucketTimes.length > 0 ? CHART_WIDTH / bucketTimes.length : CHART_WIDTH);
	/** Gap between buckets, as a fraction of a slot - shrinks to nothing on dense charts rather than eating the bars. */
	const slotPadding = $derived(bucketTimes.length > 120 ? 0 : slotWidth * 0.15);

	interface BarRect {
		key: string;
		x: number;
		y: number;
		width: number;
		height: number;
		color: string;
	}

	const bars = $derived.by((): BarRect[] => {
		const out: BarRect[] = [];
		const zeroY = yFor(0);
		values.forEach((row, b) => {
			const slotX = b * slotWidth + slotPadding / 2;
			const inner = slotWidth - slotPadding;
			if (stacked) {
				let pos = 0;
				let neg = 0;
				row.forEach((v, s) => {
					if (v == null || v === 0) return;
					const from = v >= 0 ? pos : neg;
					const to = from + v;
					if (v >= 0) pos = to;
					else neg = to;
					const y1 = yFor(from);
					const y2 = yFor(to);
					out.push({ key: `${b}:${s}`, x: slotX, y: Math.min(y1, y2), width: inner, height: Math.abs(y1 - y2), color: seriesColor(visible[s].label) });
				});
			} else {
				const barWidth = inner / Math.max(1, visible.length);
				row.forEach((v, s) => {
					if (v == null) return;
					const y = yFor(v);
					out.push({ key: `${b}:${s}`, x: slotX + s * barWidth, y: Math.min(y, zeroY), width: barWidth, height: Math.abs(zeroY - y), color: seriesColor(visible[s].label) });
				});
			}
		});
		return out;
	});

	let hoverIndex = $state<number | null>(null);
	const safeHoverIndex = $derived(hoverIndex !== null && hoverIndex < bucketTimes.length ? hoverIndex : null);

	function handlePointerMove(e: PointerEvent) {
		if (bucketTimes.length === 0) return;
		const rect = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
		const fraction = (e.clientX - rect.left) / rect.width;
		hoverIndex = Math.min(bucketTimes.length - 1, Math.max(0, Math.floor(fraction * bucketTimes.length)));
	}

	function formatBucketTime(time: number): string {
		return new Date(time).toLocaleString(undefined, { hour12: false, month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
	}
</script>

<div class="flex min-w-0 flex-1 flex-col">
	<div class="relative">
		<div class="text-muted-foreground pointer-events-none absolute inset-y-0 left-0 w-10 text-[10px]" style="height: {CHART_HEIGHT}px">
			{#each ticks.values as tick (tick)}
				{@const label = formatAtScale(tick, axisScale)}
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
							aria-label={stacked ? m.panelVisualization_stackedBarAriaLabel() : m.panelVisualization_barAriaLabel()}
							onpointermove={handlePointerMove}
							onpointerleave={() => (hoverIndex = null)}
						>
							{#each ticks.values as tick (tick)}
								<line x1="0" y1={yFor(tick)} x2={CHART_WIDTH} y2={yFor(tick)} class="text-border" stroke="currentColor" stroke-width="1" vector-effect="non-scaling-stroke" />
							{/each}
							{#if safeHoverIndex !== null}
								<rect x={safeHoverIndex * slotWidth} y={PEAK_Y} width={slotWidth} height={BASELINE_Y - PEAK_Y} class="text-muted" fill="currentColor" opacity="0.5" />
							{/if}
							<ThresholdOverlay {thresholds} {yFor} {minValue} {maxValue} width={CHART_WIDTH} peakY={PEAK_Y} baselineY={BASELINE_Y} />
							{#each bars as bar (bar.key)}
								<rect x={bar.x} y={bar.y} width={bar.width} height={bar.height} fill={bar.color} />
							{/each}
						</svg>
					{/snippet}
				</Tooltip.Trigger>
				{#if safeHoverIndex !== null}
					<Tooltip.Content>
						<div class="flex flex-col gap-0.5">
							<span class="font-medium">{formatBucketTime(bucketTimes[safeHoverIndex])}</span>
							{#each visible as s, i (s.label)}
								{@const v = values[safeHoverIndex][i]}
								{#if v != null}
									{@const match = matchThreshold(thresholds, v)}
									<span class="flex items-center gap-1.5">
										<span class="inline-block h-2 w-2 shrink-0 rounded-sm" style="background: {seriesColor(s.label)};"></span>
										{s.displayLabel}:
										<span class={match ? 'font-semibold' : undefined} style={match ? `color: ${thresholdColorValue(match.color)};` : undefined}>
											{formatAtScale(v, axisScale)}
										</span>
									</span>
								{/if}
							{/each}
						</div>
					</Tooltip.Content>
				{/if}
			</Tooltip.Root>
		</Tooltip.Provider>
	</div>

	{#if visible.length > 1 || hiddenCount > 0}
		<div class="text-muted-foreground mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs">
			{#each visible as s (s.label)}
				<span class="flex min-w-0 items-center gap-1.5">
					<span class="inline-block h-2 w-2 shrink-0 rounded-sm" style="background: {seriesColor(s.label)};"></span>
					<span class="truncate" title={s.label}>{s.displayLabel}</span>
				</span>
			{/each}
			{#if hiddenCount > 0}
				<span>{m.panelVisualization_hiddenSeries({ count: hiddenCount })}</span>
			{/if}
		</div>
	{/if}
</div>
