<script lang="ts">
	// One of the host drill-down sheet's four small charts. Same hand-rolled SVG approach
	// and $lib/metrics/axis.ts scaling as resources/HostTrendChart.svelte (this dashboard has
	// no charting library), but plotted against real time rather than array index, and with
	// gaps where a bucket has no value - the server omits empty buckets and nulls a column
	// with no data, and drawing straight through either would invent readings.
	import { formatAtScale, niceAxisTicks, resolveAxisScale } from '$lib/metrics/axis';
	import * as m from '$lib/paraglide/messages';

	interface Point {
		time: number; // epoch ms
		value: number | null;
	}

	interface Props {
		label: string;
		/** '%' for utilization, null for raw load - fed straight to resolveAxisScale. */
		unit: string | null;
		points: Point[];
		/** The requested window, so the x-axis spans it even when data only covers part of it. */
		fromMs: number;
		toMs: number;
		/** Optional epoch-ms instant drawn as a vertical line - the log event detail view marks its log's timestamp with it. */
		markerMs?: number;
	}

	let { label, unit, points, fromMs, toMs, markerMs }: Props = $props();

	const CHART_WIDTH = 400;
	const CHART_HEIGHT = 96;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	const present = $derived(points.filter((p): p is { time: number; value: number } => p.value != null));
	const peakValue = $derived(Math.max(0, ...present.map((p) => p.value)));
	const axisScale = $derived(resolveAxisScale(unit, peakValue));
	const ticks = $derived(niceAxisTicks(0, peakValue, axisScale, 3));
	const maxValue = $derived(Math.max(1e-9, ticks.max));

	function xFor(time: number): number {
		const span = Math.max(1, toMs - fromMs);
		return ((time - fromMs) / span) * CHART_WIDTH;
	}

	function yFor(value: number): number {
		return BASELINE_Y - (value / maxValue) * (BASELINE_Y - PEAK_Y);
	}

	// A null value ends the current segment; the next non-null value starts a new one ('M').
	const path = $derived.by(() => {
		let d = '';
		let penDown = false;
		for (const p of points) {
			if (p.value == null) {
				penDown = false;
				continue;
			}
			d += `${penDown ? 'L' : 'M'} ${xFor(p.time)} ${yFor(p.value)} `;
			penDown = true;
		}
		return d.trim();
	});

	let hover = $state<{ time: number; value: number } | null>(null);

	function handlePointerMove(e: PointerEvent) {
		if (present.length === 0) return;
		const rect = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
		const time = fromMs + ((e.clientX - rect.left) / rect.width) * (toMs - fromMs);
		hover = present.reduce((best, p) => (Math.abs(p.time - time) < Math.abs(best.time - time) ? p : best));
	}

	const latest = $derived(present.at(-1) ?? null);
	const shown = $derived(hover ?? latest);

	function formatTime(time: number): string {
		return new Date(time).toLocaleTimeString(undefined, { hour12: false, hour: '2-digit', minute: '2-digit' });
	}
</script>

<div class="rounded-md border p-3">
	<div class="mb-2 flex items-baseline justify-between gap-2">
		<h3 class="text-sm font-medium">{label}</h3>
		{#if shown}
			<span class="text-muted-foreground text-xs tabular-nums">
				{#if hover}{formatTime(hover.time)} · {/if}<span class="text-foreground font-medium">{formatAtScale(shown.value, axisScale)}</span>
			</span>
		{/if}
	</div>

	{#if present.length === 0}
		<div class="text-muted-foreground flex items-center justify-center text-xs" style="height: {CHART_HEIGHT}px">
			{m.hostsPage_chartNoData()}
		</div>
	{:else}
		<div class="flex gap-2">
			<div class="text-muted-foreground relative w-10 shrink-0 text-right text-xs tabular-nums" style="height: {CHART_HEIGHT}px">
				{#each ticks.values as tick (tick)}
					<span class="absolute inset-x-0 -translate-y-1/2 truncate leading-none" style="top: {yFor(tick)}px">
						{formatAtScale(tick, axisScale)}
					</span>
				{/each}
			</div>
			<svg
				viewBox="0 0 {CHART_WIDTH} {CHART_HEIGHT}"
				preserveAspectRatio="none"
				class="w-full min-w-0"
				style="height: {CHART_HEIGHT}px"
				role="img"
				aria-label={label}
				onpointermove={handlePointerMove}
				onpointerleave={() => (hover = null)}
			>
				{#each ticks.values as tick (tick)}
					<line x1="0" y1={yFor(tick)} x2={CHART_WIDTH} y2={yFor(tick)} class="text-border" stroke="currentColor" stroke-width="1" vector-effect="non-scaling-stroke" />
				{/each}
				{#if markerMs != null && markerMs >= fromMs && markerMs <= toMs}
					<line x1={xFor(markerMs)} y1="0" x2={xFor(markerMs)} y2={CHART_HEIGHT} class="text-destructive" stroke="currentColor" stroke-width="1.5" vector-effect="non-scaling-stroke" />
				{/if}
				<path d={path} fill="none" stroke="var(--primary)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" vector-effect="non-scaling-stroke" />
				{#if present.length === 1}
					<circle cx={xFor(present[0].time)} cy={yFor(present[0].value)} r="3" fill="var(--primary)" />
				{/if}
				{#if hover}
					<line x1={xFor(hover.time)} y1={PEAK_Y} x2={xFor(hover.time)} y2={BASELINE_Y} class="text-muted-foreground" stroke="currentColor" stroke-width="1" stroke-dasharray="2,2" vector-effect="non-scaling-stroke" />
					<circle cx={xFor(hover.time)} cy={yFor(hover.value)} r="3" fill="var(--primary)" />
				{/if}
			</svg>
		</div>
	{/if}
</div>
