<script lang="ts">
	// Heatmap of a histogram metric over time: X is the time bucket, Y the value range, color
	// the number of observations (see `heatmapGrid` for how the buckets land on shared rows).
	// Where the line chart's percentiles say "p95 moved", this shows the whole distribution
	// moving - a second slow mode appearing, or the body drifting up. Same hand-rolled
	// viewBox-SVG + pinned-open Tooltip technique as HistogramVisualization.svelte.
	//
	// Counts are heavy-tailed (one busy row hides the rest on a linear ramp), so the color
	// scale starts logarithmic; the corner toggle switches to linear. The toggle is a local
	// view option, not saved with the panel.
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { formatAtScale, resolveAxisScale } from '$lib/metrics/axis';
	import { SERIES_COLOR_VARS } from '$lib/metrics/chart-colors';
	import type { HeatmapGrid } from '$lib/dashboards/visualization';
	import { formatChartTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	let {
		grid,
		unit,
		decimals
	}: {
		grid: HeatmapGrid;
		unit: string | null;
		/** `DashboardPanel.decimals`, already parsed - `undefined` is auto. */
		decimals?: number;
	} = $props();

	let logScale = $state(true);

	const rows = $derived(grid.edges.length - 1);
	const columns = $derived(grid.times.length);
	const scale = $derived(resolveAxisScale(unit, Math.max(Math.abs(grid.edges[0]), Math.abs(grid.edges[grid.edges.length - 1]))));

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 180;
	const colWidth = $derived(CHART_WIDTH / Math.max(1, columns));
	const rowHeight = $derived(CHART_HEIGHT / Math.max(1, rows));

	/** 0..1 intensity of a count under the active color scale; 0 = nothing to draw. */
	function intensity(count: number): number {
		if (count <= 0 || grid.peak <= 0) return 0;
		const ratio = logScale ? Math.log1p(count) / Math.log1p(grid.peak) : count / grid.peak;
		return Math.max(0.08, Math.min(1, ratio));
	}

	// Row 0 is the lowest value, drawn at the bottom.
	function yFor(row: number): number {
		return CHART_HEIGHT - (row + 1) * rowHeight;
	}

	const cells = $derived(
		grid.cells.flatMap((column, c) =>
			column.flatMap((count, r) => {
				const opacity = intensity(count);
				return opacity > 0 ? [{ key: `${c}:${r}`, x: c * colWidth, y: yFor(r), opacity }] : [];
			})
		)
	);

	/** Edge labels every row boundary only while they fit; otherwise every n-th one. */
	const labelEvery = $derived(Math.max(1, Math.ceil(rows / 6)));
	const edgeLabels = $derived(grid.edges.map((edge, i) => ({ i, edge })).filter(({ i }) => i % labelEvery === 0 || i === rows));

	let hover = $state<{ col: number; row: number } | null>(null);
	const safeHover = $derived(hover && hover.col < columns && hover.row < rows ? hover : null);

	function handlePointerMove(e: PointerEvent) {
		if (columns === 0 || rows === 0) return;
		const rect = (e.currentTarget as SVGSVGElement).getBoundingClientRect();
		const x = (e.clientX - rect.left) / rect.width;
		const y = (e.clientY - rect.top) / rect.height;
		hover = {
			col: Math.min(columns - 1, Math.max(0, Math.floor(x * columns))),
			row: Math.min(rows - 1, Math.max(0, rows - 1 - Math.floor(y * rows)))
		};
	}

	function rangeLabel(row: number): string {
		return `${formatAtScale(grid.edges[row], scale, decimals)} – ${formatAtScale(grid.edges[row + 1], scale, decimals)}`;
	}
</script>

<div class="flex min-w-0 flex-1 flex-col">
	<div class="relative">
		<div class="text-muted-foreground pointer-events-none absolute inset-y-0 left-0 w-12 text-[10px]" style="height: {CHART_HEIGHT}px">
			{#each edgeLabels as { i, edge } (i)}
				{@const label = formatAtScale(edge, scale, decimals)}
				<span class="absolute inset-x-1 -translate-y-1/2 truncate leading-none" style="top: {CHART_HEIGHT - (i / rows) * CHART_HEIGHT}px" title={label}>{label}</span>
			{/each}
		</div>
		<Tooltip.Provider>
			<Tooltip.Root open={safeHover !== null}>
				<Tooltip.Trigger>
					{#snippet child({ props })}
						<svg
							{...props}
							viewBox="0 0 {CHART_WIDTH} {CHART_HEIGHT}"
							preserveAspectRatio="none"
							class="h-[180px] w-full min-w-0 cursor-crosshair pl-12"
							role="img"
							aria-label={m.panelVisualization_heatmapAriaLabel()}
							onpointermove={handlePointerMove}
							onpointerleave={() => (hover = null)}
						>
							<rect x="0" y="0" width={CHART_WIDTH} height={CHART_HEIGHT} class="text-muted" fill="currentColor" opacity="0.25" />
							{#each cells as cell (cell.key)}
								<rect x={cell.x} y={cell.y} width={colWidth + 0.5} height={rowHeight + 0.5} fill="var({SERIES_COLOR_VARS[0]})" fill-opacity={cell.opacity} />
							{/each}
							{#if safeHover}
								<rect
									x={safeHover.col * colWidth}
									y={yFor(safeHover.row)}
									width={colWidth}
									height={rowHeight}
									fill="none"
									stroke="currentColor"
									class="text-foreground"
									stroke-width="1.5"
									vector-effect="non-scaling-stroke"
								/>
							{/if}
						</svg>
					{/snippet}
				</Tooltip.Trigger>
				{#if safeHover}
					{@const count = grid.cells[safeHover.col][safeHover.row]}
					<Tooltip.Content>
						<div class="flex flex-col gap-0.5">
							<span class="font-medium">{formatChartTime(grid.times[safeHover.col])}</span>
							<span>{rangeLabel(safeHover.row)}</span>
							<span>{m.panelVisualization_heatmapCount({ count: Math.round(count * 100) / 100 })}</span>
						</div>
					</Tooltip.Content>
				{/if}
			</Tooltip.Root>
		</Tooltip.Provider>
	</div>

	<div class="text-muted-foreground mt-1 ml-12 flex items-center justify-between gap-2 text-[10px]">
		<span class="leading-none whitespace-nowrap">{formatChartTime(grid.times[0])}</span>
		<button type="button" class="hover:text-foreground leading-none underline-offset-2 hover:underline" onclick={() => (logScale = !logScale)}>
			{logScale ? m.panelVisualization_heatmapScaleLog() : m.panelVisualization_heatmapScaleLinear()}
		</button>
		<span class="leading-none whitespace-nowrap">{formatChartTime(grid.times[columns - 1])}</span>
	</div>
</div>
