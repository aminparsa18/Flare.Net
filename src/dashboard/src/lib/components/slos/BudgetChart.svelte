<script lang="ts">
	// The error budget remaining across the SLO window: 100% at the top, 0% (spent) as a dashed
	// line, overspend shown below it. A plain SVG area chart - one series, no interaction beyond
	// the end-point label, so it doesn't need the metrics chart machinery.
	import type { BudgetPoint } from '$lib/slos/budget';
	import { formatBudget } from '$lib/slos/budget';
	import { formatDateTimeMinutes } from '$lib/time/format';

	let { points }: { points: BudgetPoint[] } = $props();

	const W = 640;
	const H = 160;
	const PAD = { left: 40, right: 8, top: 8, bottom: 16 };

	const geometry = $derived.by(() => {
		if (points.length < 2) return null;
		const t0 = points[0].timeUnixMs;
		const t1 = points[points.length - 1].timeUnixMs;
		const min = Math.min(0, ...points.map((p) => p.remaining));
		// Plot at most a modest overspend; the number itself is in the label.
		const lo = Math.max(min, -1);
		const x = (t: number) => PAD.left + ((t - t0) / (t1 - t0 || 1)) * (W - PAD.left - PAD.right);
		const y = (v: number) => PAD.top + ((1 - Math.max(v, lo)) / (1 - lo)) * (H - PAD.top - PAD.bottom);
		const line = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.timeUnixMs).toFixed(1)},${y(p.remaining).toFixed(1)}`).join(' ');
		const zeroY = y(0);
		return { line, zeroY, topY: y(1), t0, t1, last: points[points.length - 1], lastX: x(t1), lastY: y(points[points.length - 1].remaining) };
	});
</script>

{#if geometry}
	<svg viewBox="0 0 {W} {H}" class="h-40 w-full" role="img" aria-label="Error budget remaining">
		<line x1={PAD.left} x2={W - PAD.right} y1={geometry.topY} y2={geometry.topY} class="stroke-border" stroke-width="1" />
		<line x1={PAD.left} x2={W - PAD.right} y1={geometry.zeroY} y2={geometry.zeroY} class="stroke-destructive/60" stroke-width="1" stroke-dasharray="4 3" />
		<text x={PAD.left - 4} y={geometry.topY + 3} text-anchor="end" class="fill-muted-foreground text-[10px]">100%</text>
		<text x={PAD.left - 4} y={geometry.zeroY + 3} text-anchor="end" class="fill-muted-foreground text-[10px]">0%</text>
		<path d={geometry.line} fill="none" class={geometry.last.remaining < 0 ? 'stroke-destructive' : 'stroke-primary'} stroke-width="1.5" />
		<circle cx={geometry.lastX} cy={geometry.lastY} r="3" class={geometry.last.remaining < 0 ? 'fill-destructive' : 'fill-primary'} />
		<text x={PAD.left} y={H - 3} class="fill-muted-foreground text-[10px]">{formatDateTimeMinutes(geometry.t0)}</text>
		<text x={W - PAD.right} y={H - 3} text-anchor="end" class="fill-muted-foreground text-[10px]">{formatDateTimeMinutes(geometry.t1)}</text>
	</svg>
	<p class="text-muted-foreground text-xs">{formatBudget(geometry.last.remaining)}</p>
{/if}
