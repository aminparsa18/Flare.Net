<script lang="ts">
	// Hand-rolled chart - same viewBox/array-index-x/hover-tooltip technique
	// IngestionChart/MetricChart already establish. Lines: new-part bytes per day for the
	// top 5 tables by total size (dataviz skill's categorical palette caps out at 5 slots
	// same as MetricChart's series cap) - the rest fold into a "+N not shown" note, same
	// convention.
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { Spinner } from '$lib/components/ui/spinner';
	import { indexingContext } from '$lib/indexing/context';
	import { formatBytes } from '$lib/indexing/format';

	const indexing = indexingContext.get();

	const SERIES_COLOR_VARS = ['--chart-1', '--chart-2', '--chart-3', '--chart-4', '--chart-5'] as const;
	const MAX_SERIES = SERIES_COLOR_VARS.length;

	interface LineSpec {
		tableName: string;
		color: string;
		points: { day: string; bytes: number }[];
	}

	// Top tables by total growth-window bytes, in the fixed order `IndexingTablesTable`
	// already shows (server-sorted by total_bytes DESC) - keeps the two panels' table
	// ordering/coloring consistent rather than re-deriving a different order here.
	const topTableNames = $derived((indexing.stats?.tables ?? []).slice(0, MAX_SERIES).map((t) => t.tableName));
	const hiddenTableCount = $derived(Math.max(0, (indexing.stats?.tables.length ?? 0) - MAX_SERIES));

	const days = $derived([...new Set((indexing.stats?.growth ?? []).map((p) => p.day))].sort());

	const lines = $derived.by((): LineSpec[] => {
		const growth = indexing.stats?.growth ?? [];
		return topTableNames.map((tableName, i) => ({
			tableName,
			color: `var(${SERIES_COLOR_VARS[i]})`,
			points: days.map((day) => ({
				day,
				bytes: growth.find((p) => p.day === day && p.tableName === tableName)?.bytes ?? 0
			}))
		}));
	});

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 140;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	const peakValue = $derived(Math.max(0, ...lines.flatMap((l) => l.points.map((p) => p.bytes))));
	const maxValue = $derived(Math.max(1e-9, peakValue));

	function xFor(index: number): number {
		const count = days.length;
		if (count <= 1) return CHART_WIDTH / 2;
		return (index / (count - 1)) * CHART_WIDTH;
	}

	function yFor(value: number): number {
		return BASELINE_Y - (value / maxValue) * (BASELINE_Y - PEAK_Y);
	}

	function pathFor(points: { bytes: number }[]): string {
		return points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${xFor(i)} ${yFor(p.bytes)}`).join(' ');
	}

	let hoverIndex = $state<number | null>(null);

	function handlePointerMove(e: PointerEvent) {
		if (days.length === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		const rect = svg.getBoundingClientRect();
		const fraction = (e.clientX - rect.left) / rect.width;
		hoverIndex = Math.min(days.length - 1, Math.max(0, Math.round(fraction * (days.length - 1))));
	}

	function formatDay(iso: string): string {
		return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
	}
</script>

<div class="flex flex-col gap-2 px-4 pb-4">
	<h2 class="text-sm font-medium">Growth (last 30 days)</h2>

	{#if indexing.loading && !indexing.stats}
		<div class="flex h-[140px] items-center justify-center">
			<Spinner />
		</div>
	{:else if !indexing.stats?.growthAvailable}
		<p class="text-muted-foreground text-xs">
			Not available - <code class="font-mono">system.part_log</code> isn't queryable on this ClickHouse deployment.
		</p>
	{:else if days.length === 0}
		<div class="text-muted-foreground flex h-[140px] items-center justify-center text-xs">No new data in the last 30 days</div>
	{:else}
		<div class="text-muted-foreground flex flex-wrap items-center gap-4 text-xs">
			{#each lines as line (line.tableName)}
				<span class="flex items-center gap-1.5">
					<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"></span>
					{line.tableName}
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
							class="h-[140px] w-full"
							role="img"
							aria-label="New part bytes per day, by table"
							onpointermove={handlePointerMove}
							onpointerleave={() => (hoverIndex = null)}
						>
							{#each [PEAK_Y, (PEAK_Y + BASELINE_Y) / 2, BASELINE_Y] as gridY (gridY)}
								<line
									x1="0"
									y1={gridY}
									x2={CHART_WIDTH}
									y2={gridY}
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

							{#each lines as line (line.tableName)}
								<path
									d={pathFor(line.points)}
									fill="none"
									stroke={line.color}
									stroke-width="2"
									stroke-linecap="round"
									stroke-linejoin="round"
									vector-effect="non-scaling-stroke"
								/>
							{/each}
						</svg>
					{/snippet}
				</Tooltip.Trigger>
				{#if hoverIndex !== null}
					<Tooltip.Content>
						<div class="flex flex-col gap-0.5">
							<span class="font-medium">{formatDay(days[hoverIndex])}</span>
							{#each lines as line (line.tableName)}
								<span class="flex items-center gap-1.5">
									<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"></span>
									{line.tableName}: {formatBytes(line.points[hoverIndex!]?.bytes ?? 0)}
								</span>
							{/each}
						</div>
					</Tooltip.Content>
				{/if}
			</Tooltip.Root>
		</Tooltip.Provider>

		{#if hiddenTableCount > 0}
			<p class="text-muted-foreground text-xs">+{hiddenTableCount} more table(s) not shown ({MAX_SERIES} max).</p>
		{/if}
	{/if}
</div>
