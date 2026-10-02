<script lang="ts">
	// Pie visualization: each series' share of the query, one slice per series reduced to a
	// single number by the panel's reducer. Colored by rank (largest slice = --chart-1), not
	// by series identity like the line/bar charts - with at most 5 slices there's no hash
	// collision to live with, and a pie reads best when neighbouring slices never share a hue.
	// Past 5 series the smallest fold into "Other" (see `pieSlices`). Values carry the
	// metric's unit in the legend and tooltip.
	//
	// The legend sits beside the pie unless the panel's `legendPosition` says otherwise, and a
	// `seriesColors` override beats the rank color for its slice (see `$lib/dashboards/legend.ts`).
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { rankColor, SERIES_COLOR_VARS } from '$lib/metrics/chart-colors';
	import { formatValue, pieSlicePath, pieSlices } from '$lib/dashboards/visualization';
	import { seriesColorOverride, type LegendPosition } from '$lib/dashboards/legend';
	import type { ThresholdColor } from '$lib/dashboards/thresholds';
	import * as m from '$lib/paraglide/messages';

	let {
		entries,
		unit,
		decimals,
		legendPosition = 'right',
		seriesColors = {}
	}: {
		/** `key` is the series' full identity (`seriesLabel`) - what a color override is keyed by. */
		entries: { label: string; value: number; key: string }[];
		unit: string | null;
		/** `DashboardPanel.decimals`, already parsed - `undefined` is auto. */
		decimals?: number;
		legendPosition?: LegendPosition;
		seriesColors?: Record<string, ThresholdColor>;
	} = $props();

	const slices = $derived(pieSlices(entries, SERIES_COLOR_VARS.length, m.panelVisualization_pieOther()));
	const total = $derived(slices.reduce((sum, s) => sum + s.value, 0));

	const arcs = $derived.by(() => {
		let start = 0;
		return slices.map((slice, i) => {
			const end = start + slice.value / total;
			const arc = {
				...slice,
				color: slice.other ? 'var(--muted-foreground)' : ((slice.key && seriesColorOverride(seriesColors, slice.key)) ?? rankColor(i)),
				path: pieSlicePath(50, 50, 48, start, end),
				percent: (slice.value / total) * 100
			};
			start = end;
			return arc;
		});
	});

	let hovered = $state<number | null>(null);
	const hoveredArc = $derived(hovered !== null && hovered < arcs.length ? arcs[hovered] : null);

	function formatPercent(p: number): string {
		return `${p < 10 ? p.toFixed(1) : Math.round(p)}%`;
	}
</script>

{#if slices.length === 0}
	<div class="text-muted-foreground flex flex-1 items-center justify-center text-xs">{m.panelVisualization_pieNoPositive()}</div>
{:else}
	<div class="flex min-h-0 flex-1 items-center gap-4 p-2 {legendPosition === 'bottom' ? 'flex-col' : ''}">
		<Tooltip.Provider>
			<Tooltip.Root open={hoveredArc !== null}>
				<Tooltip.Trigger>
					{#snippet child({ props })}
						<svg
							{...props}
							viewBox="0 0 100 100"
							class="aspect-square min-h-0 {legendPosition === 'bottom' ? 'max-h-40 min-w-0 flex-1' : 'h-full max-h-56 shrink-0'}"
							role="img"
							aria-label={m.panelVisualization_pieAriaLabel()}
						>
							{#each arcs as arc, i (arc.label)}
								<path
									d={arc.path}
									fill={arc.color}
									stroke="var(--background)"
									stroke-width="0.75"
									opacity={hovered === null || hovered === i ? 1 : 0.55}
									role="presentation"
									onpointerenter={() => (hovered = i)}
									onpointerleave={() => (hovered = null)}
								/>
							{/each}
						</svg>
					{/snippet}
				</Tooltip.Trigger>
				{#if hoveredArc}
					<Tooltip.Content>
						<span class="font-medium">{hoveredArc.label}</span>: {formatValue(hoveredArc.value, unit, decimals)} ({formatPercent(hoveredArc.percent)})
					</Tooltip.Content>
				{/if}
			</Tooltip.Root>
		</Tooltip.Provider>
		{#if legendPosition !== 'hidden'}
			<ul class="flex min-w-0 flex-col gap-1 overflow-y-auto text-xs {legendPosition === 'bottom' ? 'w-full max-w-sm shrink-0' : 'flex-1'}">
				{#each arcs as arc, i (arc.label)}
					<li class="flex min-w-0 items-center gap-1.5" onpointerenter={() => (hovered = i)} onpointerleave={() => (hovered = null)}>
						<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {arc.color};"></span>
						<span class="min-w-0 flex-1 truncate" title={arc.label}>{arc.label}</span>
						<span class="shrink-0 tabular-nums">{formatValue(arc.value, unit, decimals)}</span>
						<span class="text-muted-foreground w-12 shrink-0 text-right tabular-nums">{formatPercent(arc.percent)}</span>
					</li>
				{/each}
			</ul>
		{/if}
	</div>
{/if}
