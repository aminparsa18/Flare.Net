<script lang="ts">
	// "Resource trends" - the Host overview panel's trend chart. Modeled closely on
	// components/metrics/MetricChart.svelte's hand-rolled SVG mechanics (this dashboard has
	// no charting library - hand-rolling is the established precedent), trimmed to a single
	// line since only one metric is ever plotted at a time (see MAX_SERIES/legend remarks
	// there for the multi-line case this doesn't need). Reuses $lib/metrics/axis.ts
	// unchanged for Y-axis scaling/formatting - it already handles both unit shapes this
	// needs: '%' for CPU/Memory/Disk (peak-scaled ticks, e.g. 0/10/20/30% on a mostly-idle
	// host - same "scale to observed peak, not a hardcoded 0-100 domain" convention
	// MetricChart already uses everywhere else) and 'By/s' for Network (its rate-unit
	// branch already splits numerator/denominator and picks KB/MB/GB automatically).
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { Button } from '$lib/components/ui/button';
	import { formatAtScale, niceAxisTicks, resolveAxisScale } from '$lib/metrics/axis';
	import type { HostStatsHistoryPoint } from '$lib/api';

	let { history }: { history: HostStatsHistoryPoint[] } = $props();

	type MetricKey = 'cpu' | 'memory' | 'disk' | 'network';

	// unit: fed straight to resolveAxisScale (see the file header) - '%' for the three
	// bytes-based metrics (already precomputed server-side, see HostStatsHistoryPoint.cs),
	// 'By/s' for Network (still in its natural unit - no fixed ceiling to normalize
	// against).
	const METRICS: Record<MetricKey, { label: string; unit: string; valueOf: (p: HostStatsHistoryPoint) => number }> = {
		cpu: { label: 'CPU', unit: '%', valueOf: (p) => p.cpuUsagePercent },
		memory: { label: 'Memory', unit: '%', valueOf: (p) => p.memoryUsedPercent },
		disk: { label: 'Disk', unit: '%', valueOf: (p) => p.diskUsedPercent },
		network: { label: 'Network', unit: 'By/s', valueOf: (p) => p.networkBytesPerSecond }
	};
	const METRIC_ORDER: MetricKey[] = ['cpu', 'memory', 'disk', 'network'];

	let metric = $state<MetricKey>('cpu');

	interface PlotPoint {
		time: number; // epoch ms
		raw: number;
	}

	// Already in the right (ascending, uniformly-sampled) order - HostStatsState only ever
	// appends, and the backfill it seeds from is server-sorted too - so unlike
	// MetricChart's bucketTimes (which dedupes/sorts several *lines*' worth of buckets),
	// array-index position here is exactly right, not an approximation.
	const points = $derived<PlotPoint[]>(
		history.map((p) => ({ time: new Date(p.timestamp).getTime(), raw: METRICS[metric].valueOf(p) }))
	);

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 140;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	const peakValue = $derived(Math.max(0, ...points.map((p) => p.raw)));
	const axisScale = $derived(resolveAxisScale(METRICS[metric].unit, peakValue));
	const ticks = $derived(niceAxisTicks(peakValue, axisScale));
	const maxValue = $derived(Math.max(1e-9, ticks.max));

	function xFor(index: number): number {
		return points.length <= 1 ? CHART_WIDTH / 2 : (index / (points.length - 1)) * CHART_WIDTH;
	}

	function yFor(raw: number): number {
		return BASELINE_Y - (raw / maxValue) * (BASELINE_Y - PEAK_Y);
	}

	const path = $derived(points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${xFor(i)} ${yFor(p.raw)}`).join(' '));

	let hoverIndex = $state<number | null>(null);

	function handlePointerMove(e: PointerEvent) {
		if (points.length === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		const rect = svg.getBoundingClientRect();
		const fraction = (e.clientX - rect.left) / rect.width;
		hoverIndex = Math.min(points.length - 1, Math.max(0, Math.round(fraction * (points.length - 1))));
	}

	function formatPointTime(time: number): string {
		return new Date(time).toLocaleString(undefined, {
			hour12: false,
			month: 'short',
			day: 'numeric',
			hour: '2-digit',
			minute: '2-digit',
			second: '2-digit'
		});
	}

	function formatValue(n: number): string {
		return formatAtScale(n, axisScale);
	}

	// "-Xm"/"-Xh" from the oldest point's actual age, not a hardcoded "-1h" - honest about
	// a freshly-started Flare.Api not having a full hour of history yet (the window fills
	// in and this label settles at "-1h" on its own once it does, since the server trims
	// to exactly that).
	function formatAgoLabel(time: number): string {
		const minutes = Math.round((Date.now() - time) / 60000);
		if (minutes < 1) return 'now';
		if (minutes < 60) return `-${minutes}m`;
		return `-${Math.round(minutes / 60)}h`;
	}
</script>

<div class="mt-4 border-t pt-3">
	<div class="mb-2 flex items-center justify-between gap-2">
		<h3 class="text-sm font-medium">Resource trends</h3>
		<div class="bg-muted flex items-center gap-0.5 rounded-md p-0.5">
			{#each METRIC_ORDER as key (key)}
				<Button variant={metric === key ? 'secondary' : 'ghost'} size="sm" onclick={() => (metric = key)}>
					{METRICS[key].label}
				</Button>
			{/each}
		</div>
	</div>

	{#if points.length < 2}
		<div class="text-muted-foreground flex h-[140px] items-center justify-center text-xs">
			Not enough data yet - the chart fills in over the next couple of samples.
		</div>
	{:else}
		<div class="flex gap-2">
			<div
				class="text-muted-foreground relative w-14 shrink-0 text-right text-xs whitespace-nowrap tabular-nums"
				style="height: {CHART_HEIGHT}px"
			>
				{#each ticks.values as tick (tick)}
					<span class="absolute inset-x-1 -translate-y-1/2 truncate leading-none" style="top: {yFor(tick)}px">
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
								class="h-[140px] w-full min-w-0"
								role="img"
								aria-label="{METRICS[metric].label} over the last hour"
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
										x1={xFor(hoverIndex)}
										y1={PEAK_Y}
										x2={xFor(hoverIndex)}
										y2={BASELINE_Y}
										class="text-muted-foreground"
										stroke="currentColor"
										stroke-width="1"
										stroke-dasharray="2,2"
										vector-effect="non-scaling-stroke"
									/>
								{/if}

								<path d={path} fill="none" stroke="var(--primary)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" vector-effect="non-scaling-stroke" />

								{#if hoverIndex !== null}
									<circle cx={xFor(hoverIndex)} cy={yFor(points[hoverIndex].raw)} r="3" fill="var(--primary)" />
								{/if}
							</svg>
						{/snippet}
					</Tooltip.Trigger>
					{#if hoverIndex !== null}
						<Tooltip.Content>
							<span>{formatPointTime(points[hoverIndex].time)} · {formatValue(points[hoverIndex].raw)}</span>
						</Tooltip.Content>
					{/if}
				</Tooltip.Root>
			</Tooltip.Provider>
		</div>
		<div class="text-muted-foreground mt-1 ml-16 flex justify-between text-xs">
			<span>{formatAgoLabel(points[0].time)}</span>
			<span>now</span>
		</div>
	{/if}
</div>
