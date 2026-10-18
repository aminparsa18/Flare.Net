<svelte:options namespace="svg" />

<script lang="ts">
	// A hollow ring per exemplar inside a chart's own <svg>, in that chart's viewBox coordinates (see
	// `#lib/metrics/exemplars.ts`; ADR-0242, ADR-0244). Each is a link to its trace. Pointer events stop here so
	// opening a trace is not also a drag-to-zoom or a dashboard panel click.
	import type { ExemplarMark } from '#lib/metrics/exemplars.ts';
	import * as m from '#lib/paraglide/messages.js';

	let { marks }: { marks: readonly ExemplarMark[] } = $props();
</script>

{#each marks as mark, i (i)}
	<a href={mark.href} aria-label={m.metricChart_exemplarAriaLabel({ trace: mark.label })} onpointerdown={(e) => e.stopPropagation()} onpointerup={(e) => e.stopPropagation()} class="cursor-pointer">
		<circle cx={mark.x} cy={mark.y} r="4" style="fill: var(--background); stroke: var(--foreground);" stroke-width="1.5" vector-effect="non-scaling-stroke">
			<title>{mark.title}</title>
		</circle>
	</a>
{/each}
