<svelte:options namespace="svg" />

<script lang="ts">
	// Draws a dashboard panel's visual threshold rules (DashboardPanel.thresholds - see
	// `$lib/dashboards/thresholds.ts`) inside MetricChart's/FormulaChart's own <svg>, in that
	// chart's own viewBox coordinates: one faint shaded region per rule on the side its
	// operator points at (above for `>`/`>=`, below for `<`/`<=`), plus a dashed line at the
	// rule's value. Rendered before the series paths, so data always draws on top.
	//
	// Thresholds never widen the chart's Y domain - a rule whose line falls outside the
	// visible range just isn't drawn (its region is still shaded if it covers the visible
	// range, e.g. `< 1000` on a chart topping out at 50). Pulling an out-of-range threshold
	// into view is what the panel's soft Y-axis min/max is for.
	import { thresholdColorValue, thresholdShadesAbove, type PanelThreshold } from '$lib/dashboards/thresholds';

	let {
		thresholds,
		yFor,
		minValue,
		maxValue,
		width,
		peakY,
		baselineY
	}: {
		thresholds: readonly PanelThreshold[];
		yFor: (raw: number) => number;
		/** The chart's rendered (post-"nice"-rounding) Y domain. */
		minValue: number;
		maxValue: number;
		width: number;
		peakY: number;
		baselineY: number;
	} = $props();

	const clampY = (y: number) => Math.min(baselineY, Math.max(peakY, y));
</script>

{#each thresholds as threshold (threshold.id)}
	{@const color = thresholdColorValue(threshold.color)}
	{@const lineY = clampY(yFor(threshold.value))}
	{@const regionTop = thresholdShadesAbove(threshold) ? peakY : lineY}
	{@const regionBottom = thresholdShadesAbove(threshold) ? lineY : baselineY}
	{#if regionBottom > regionTop}
		<rect x="0" y={regionTop} width={width} height={regionBottom - regionTop} fill={color} fill-opacity="0.08" />
	{/if}
	{#if threshold.value >= minValue && threshold.value <= maxValue}
		<line
			x1="0"
			y1={lineY}
			x2={width}
			y2={lineY}
			stroke={color}
			stroke-width="1.5"
			stroke-dasharray="6,4"
			vector-effect="non-scaling-stroke"
		/>
	{/if}
{/each}
