<svelte:options namespace="svg" />

<script lang="ts">
	// Dashed vertical line per deploy inside a chart's own <svg>, in that chart's viewBox
	// coordinates (see `#lib/releases/markers.ts`). Drawn before the series so data stays on top.
	// The wide transparent stroke is the hover target for the native <title> tooltip; pointer
	// events still bubble to the chart's own handlers.
	import type { ReleaseMarker } from '#lib/releases/markers.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { markers, width, peakY, baselineY }: { markers: readonly ReleaseMarker[]; width: number; peakY: number; baselineY: number } = $props();
</script>

{#each markers as marker (marker.release.id)}
	{@const x = marker.fraction * width}
	<line
		x1={x}
		x2={x}
		y1={peakY}
		y2={baselineY}
		stroke="var(--chart-4, currentColor)"
		stroke-width="1.5"
		stroke-dasharray="4,3"
		opacity="0.8"
		vector-effect="non-scaling-stroke"
	/>
	<line x1={x} x2={x} y1={peakY} y2={baselineY} stroke="transparent" stroke-width="8" vector-effect="non-scaling-stroke">
		<title>{m.releaseMarker_title({ service: marker.release.service, version: marker.release.version, time: new Date(marker.time).toLocaleString() })}</title>
	</line>
{/each}
