<script lang="ts">
	// Crash-free session rate over time, one line per app version (ADR-0189). Plain SVG: a handful of
	// lines and a hover readout don't need the metrics chart machinery.
	import type { ReleaseHealthTrend } from '$lib/app-sessions-api';
	import { buildTrendSeries, trendAxisMin } from '$lib/app-sessions/trend';
	import { formatChartTime } from '$lib/time/format';

	let { trend }: { trend: ReleaseHealthTrend } = $props();

	const WIDTH = 720;
	const HEIGHT = 180;
	const PAD = { left: 40, right: 8, top: 8, bottom: 20 };
	const COLORS = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)', 'var(--chart-4)', 'var(--chart-5)', 'var(--muted-foreground)'];

	const series = $derived(buildTrendSeries(trend.points));
	const axisMin = $derived(trendAxisMin(series));
	const bucketMs = $derived(trend.bucketMinutes * 60_000);
	const times = $derived(trend.points.map((p) => p.bucketUnixMs));
	const tMin = $derived(times.length ? Math.min(...times) : 0);
	// One bucket past the newest point, so a single bucket still has width and the last point isn't on the edge.
	const tMax = $derived(times.length ? Math.max(...times) + bucketMs : 1);

	function x(t: number): number {
		return PAD.left + ((t - tMin) / Math.max(tMax - tMin, 1)) * (WIDTH - PAD.left - PAD.right);
	}

	function y(rate: number): number {
		return PAD.top + ((100 - rate) / Math.max(100 - axisMin, 0.0001)) * (HEIGHT - PAD.top - PAD.bottom);
	}

	const ticks = $derived([axisMin, (axisMin + 100) / 2, 100]);
	const showTarget = $derived(axisMin <= 99);

	let hover = $state<{ version: string; t: number; rate: number; sessions: number; crashed: number; cx: number; cy: number } | null>(null);
</script>

<div class="relative">
	<svg viewBox="0 0 {WIDTH} {HEIGHT}" class="h-44 w-full" role="img" aria-label="Crash-free sessions over time" onmouseleave={() => (hover = null)}>
		{#each ticks as tick (tick)}
			<line x1={PAD.left} x2={WIDTH - PAD.right} y1={y(tick)} y2={y(tick)} class="stroke-border" stroke-width="1" />
			<text x={PAD.left - 6} y={y(tick) + 3} text-anchor="end" class="fill-muted-foreground text-[10px]">{tick.toFixed(tick % 1 === 0 ? 0 : 1)}%</text>
		{/each}
		{#if showTarget}
			<line x1={PAD.left} x2={WIDTH - PAD.right} y1={y(99)} y2={y(99)} class="stroke-destructive/60" stroke-dasharray="4 3" stroke-width="1" />
		{/if}
		<text x={PAD.left} y={HEIGHT - 4} class="fill-muted-foreground text-[10px]">{formatChartTime(tMin, bucketMs)}</text>
		<text x={WIDTH - PAD.right} y={HEIGHT - 4} text-anchor="end" class="fill-muted-foreground text-[10px]">{formatChartTime(tMax - bucketMs, bucketMs)}</text>
		{#each series as s, i (s.version)}
			{@const color = COLORS[i % COLORS.length]}
			<polyline fill="none" stroke={color} stroke-width="1.5" points={s.points.map((p) => `${x(p.t + bucketMs / 2)},${y(p.rate)}`).join(' ')} />
			{#each s.points as p (p.t)}
				<circle
					cx={x(p.t + bucketMs / 2)}
					cy={y(p.rate)}
					r="3"
					fill={color}
					role="presentation"
					onmouseenter={() => (hover = { version: s.version, ...p, cx: x(p.t + bucketMs / 2), cy: y(p.rate) })}
				/>
			{/each}
		{/each}
	</svg>
	{#if hover}
		<div
			class="bg-popover text-popover-foreground pointer-events-none absolute z-10 rounded-md border px-2 py-1 text-xs shadow-md"
			style="left: {(hover.cx / WIDTH) * 100}%; top: {(hover.cy / HEIGHT) * 100}%; transform: translate(-50%, -120%)"
		>
			<div class="font-medium">{hover.version || '—'} · {hover.rate.toFixed(2)}%</div>
			<div class="text-muted-foreground">{formatChartTime(hover.t, bucketMs)} · {hover.crashed} / {hover.sessions}</div>
		</div>
	{/if}
	<div class="flex flex-wrap gap-x-3 gap-y-1 pt-1 text-xs">
		{#each series as s, i (s.version)}
			<span class="flex items-center gap-1">
				<span class="inline-block size-2 rounded-full" style="background: {COLORS[i % COLORS.length]}"></span>
				{s.version || '—'}
			</span>
		{/each}
	</div>
</div>
