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
	import { formatAtScale, niceAxisTicks, resolveAxisScale } from '$lib/metrics/axis';
	import { formatBucketWidthSeconds } from '$lib/logs/bucket-width';
	import type { MetricSeries } from '$lib/metrics-api';
	import * as m from '$lib/paraglide/messages';

	// yAxisMin/yAxisMax: same soft Y-axis floor/ceiling MetricChart.svelte's own props of
	// the same name apply (DashboardPanel.yAxisMin/yAxisMax) - only ever set from a
	// dashboard panel, never from the Explorer page itself, which never passes them.
	let { yAxisMin = null, yAxisMax = null }: { yAxisMin?: number | null; yAxisMax?: number | null } = $props();

	const explorer = metricsExplorerContext.get();

	// Same fixed categorical palette + identity-hash slot assignment as MetricChart's own
	// seriesColor - duplicated rather than imported/extracted, since MetricChart is a large,
	// deliberately-untouched file for this change (see this file's header comment) and the
	// function itself is a handful of lines; a shared `$lib/metrics/chart-utils.ts` is a
	// reasonable follow-up if a third chart ever needs it too.
	const SERIES_COLOR_VARS = ['--chart-1', '--chart-2', '--chart-3', '--chart-4', '--chart-5'] as const;

	function seriesColor(identity: string): string {
		let hash = 5381;
		for (let i = 0; i < identity.length; i++) {
			hash = (hash * 33) ^ identity.charCodeAt(i);
		}
		const index = Math.abs(hash) % SERIES_COLOR_VARS.length;
		return `var(${SERIES_COLOR_VARS[index]})`;
	}

	function seriesLabel(series: MetricSeries): string {
		const attrs = Object.entries(series.attributes)
			.map(([k, v]) => `${k}=${v}`)
			.join(', ');
		return attrs ? `${series.serviceName} (${attrs})` : series.serviceName;
	}

	interface LineSpec {
		label: string;
		color: string;
		points: { time: number; raw: number }[];
	}

	const lines = $derived<LineSpec[]>(
		explorer.formulaSeries.map((series) => ({
			label: seriesLabel(series),
			color: seriesColor(seriesLabel(series)),
			points: series.points.filter((p): p is typeof p & { value: number } => p.value != null).map((p) => ({ time: new Date(p.bucketStart).getTime(), raw: p.value }))
		}))
	);

	const bucketTimes = $derived([...new Set(lines.flatMap((l) => l.points.map((p) => p.time)))].sort((a, b) => a - b));
	const bucketIndexOf = $derived(new Map(bucketTimes.map((t, i) => [t, i])));

	const rawValues = $derived(lines.flatMap((l) => l.points.map((p) => p.raw)));
	const dataMax = $derived(rawValues.length > 0 ? Math.max(...rawValues) : 0);
	const dataMin = $derived(rawValues.length > 0 ? Math.min(0, ...rawValues) : 0);

	// yAxisMin/yAxisMax narrow the "nice" domain the same way MetricChart's own
	// domainMin/domainMax do - see that file's remarks for how "soft" is applied (the bound
	// only ever widens the domain outward, never clips data that falls past it).
	const domainMin = $derived(yAxisMin != null ? Math.min(yAxisMin, dataMin) : dataMin);
	const domainMax = $derived(yAxisMax != null ? Math.max(yAxisMax, dataMax) : dataMax);

	// Dimensionless - a formula result has no single declared unit of its own (its operands
	// might each have different, or no, units - e.g. `(A/B)*100` for a ratio-as-percentage
	// has no meaningful inherited unit either), so this always resolves the "no unit" branch.
	const axisScale = $derived(resolveAxisScale(null, Math.max(Math.abs(domainMin), Math.abs(domainMax))));
	const ticks = $derived(niceAxisTicks(domainMin, domainMax, axisScale));
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
		return BASELINE_Y - ((raw - minValue) / (maxValue - minValue)) * (BASELINE_Y - PEAK_Y);
	}

	function pathFor(points: LineSpec['points']): string {
		return points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${xFor(p.time)} ${yFor(p.raw)}`).join(' ');
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
		return new Date(time).toLocaleString(undefined, { hour12: false, month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
	}

	function formatValue(n: number): string {
		return formatAtScale(n, axisScale);
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
			<span>{m.metricChart_intervalLabel({ interval: formatBucketWidthSeconds(explorer.formulaIntervalSeconds) })}</span>
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
		<div class="flex min-w-0 flex-1 flex-col">
			<div class="relative flex-1">
				<div class="text-muted-foreground pointer-events-none absolute inset-y-0 left-0 w-10 text-[10px]" style="height: {CHART_HEIGHT}px">
					{#each ticks.values as tick (tick)}
						{@const label = formatAtScale(tick, axisScale)}
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

									{#each lines as line (line.label)}
										<path d={pathFor(line.points)} fill="none" stroke={line.color} stroke-width="2" stroke-linecap="round" stroke-linejoin="round" vector-effect="non-scaling-stroke" />
										{#each line.points as point (point.time)}
											<circle cx={xFor(point.time)} cy={yFor(point.raw)} r={bucketTimes.length > 60 ? 0 : 2.5} fill={line.color} />
										{/each}
									{/each}
								</svg>
							{/snippet}
						</Tooltip.Trigger>
						{#if safeHoverIndex !== null}
							<Tooltip.Content>
								<div class="flex flex-col gap-0.5">
									<span class="font-medium">{formatBucketTime(bucketTimes[safeHoverIndex])}</span>
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
		</div>
	{/if}
</div>
