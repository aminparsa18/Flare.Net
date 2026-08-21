<script lang="ts">
	// Redesigned per feedback: plotting one line per table forced the reader to eyeball five
	// wandering lines against each other and privately answer "is this normal?" themselves.
	// One line answers it for them - a single running total (or, for the Ingestion metric, a
	// single daily rate) shaped like an actual growth curve. The metric toggle (what's
	// growing: storage/rows/ingestion) and breakdown toggle (which signal: total/logs/
	// traces/metrics) replace the multi-table legend - still lets someone isolate "is it
	// traces specifically blowing up," just one axis at a time instead of all at once.
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Button } from '$lib/components/ui/button';
	import { indexingContext } from '$lib/indexing/context';
	import { formatBytes, formatCount } from '$lib/indexing/format';

	const indexing = indexingContext.get();

	type Metric = 'storage' | 'rows' | 'ingestion';
	type Breakdown = 'total' | 'logs' | 'traces' | 'metrics';

	const METRIC_OPTIONS: { value: Metric; label: string }[] = [
		{ value: 'storage', label: 'Storage' },
		{ value: 'rows', label: 'Rows' },
		{ value: 'ingestion', label: 'Ingestion' }
	];
	const METRIC_HEADING: Record<Metric, string> = {
		storage: 'Storage growth',
		rows: 'Row growth',
		ingestion: 'Ingestion'
	};

	const BREAKDOWN_OPTIONS: { value: Breakdown; label: string }[] = [
		{ value: 'total', label: 'Total' },
		{ value: 'logs', label: 'Logs' },
		{ value: 'traces', label: 'Traces' },
		{ value: 'metrics', label: 'Metrics' }
	];

	// spans is the traces table's physical name (see db/clickhouse/*.sql) - "metrics_" is a
	// prefix match rather than three hardcoded names so a future metrics_* table folds in
	// automatically instead of silently falling out of the "Metrics" breakdown.
	function matchesBreakdown(tableName: string, breakdown: Breakdown): boolean {
		switch (breakdown) {
			case 'total':
				return true;
			case 'logs':
				return tableName === 'logs';
			case 'traces':
				return tableName === 'spans';
			case 'metrics':
				return tableName.startsWith('metrics_');
		}
	}

	let metric = $state<Metric>('storage');
	let breakdown = $state<Breakdown>('total');

	const days = $derived([...new Set((indexing.stats?.growth ?? []).map((p) => p.day))].sort());

	// Storage/Rows are stock quantities - running totals across the window read as an actual
	// growth curve, the shape the redesign asked for. Ingestion stays a flow (bytes added
	// *that day*), since a running total of it would just be the Storage line again.
	const points = $derived.by((): { day: string; value: number }[] => {
		const growth = indexing.stats?.growth ?? [];
		const filtered = growth.filter((p) => matchesBreakdown(p.tableName, breakdown));
		const daily = days.map((day) => ({
			day,
			value: filtered.filter((p) => p.day === day).reduce((sum, p) => sum + (metric === 'rows' ? p.rows : p.bytes), 0)
		}));
		if (metric === 'ingestion') return daily;

		let running = 0;
		return daily.map((d) => {
			running += d.value;
			return { day: d.day, value: running };
		});
	});

	function formatValue(value: number): string {
		return metric === 'rows' ? formatCount(value) : formatBytes(value);
	}

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 140;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	const peakValue = $derived(Math.max(0, ...points.map((p) => p.value)));
	const maxValue = $derived(Math.max(1e-9, peakValue));

	function xFor(index: number): number {
		const count = days.length;
		if (count <= 1) return CHART_WIDTH / 2;
		return (index / (count - 1)) * CHART_WIDTH;
	}

	function yFor(value: number): number {
		return BASELINE_Y - (value / maxValue) * (BASELINE_Y - PEAK_Y);
	}

	function pathFor(): string {
		return points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${xFor(i)} ${yFor(p.value)}`).join(' ');
	}

	// Closes the line down to the baseline to make a fillable area - the fill is what reads
	// as "growth" at a glance, before anyone even looks at the axis.
	function areaPathFor(): string {
		if (points.length === 0) return '';
		return `${pathFor()} L ${xFor(points.length - 1)} ${BASELINE_Y} L ${xFor(0)} ${BASELINE_Y} Z`;
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
	<div class="flex flex-wrap items-center justify-between gap-2">
		<h2 class="text-sm font-medium">{METRIC_HEADING[metric]} (last 30 days)</h2>
		<div class="flex flex-wrap items-center gap-3">
			<div class="flex items-center gap-0.5 rounded-md border p-0.5">
				{#each METRIC_OPTIONS as opt (opt.value)}
					<Button variant={metric === opt.value ? 'secondary' : 'ghost'} size="xs" onclick={() => (metric = opt.value)}>
						{opt.label}
					</Button>
				{/each}
			</div>
			<div class="flex items-center gap-0.5 rounded-md border p-0.5">
				{#each BREAKDOWN_OPTIONS as opt (opt.value)}
					<Button variant={breakdown === opt.value ? 'secondary' : 'ghost'} size="xs" onclick={() => (breakdown = opt.value)}>
						{opt.label}
					</Button>
				{/each}
			</div>
		</div>
	</div>

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
		<div class="flex gap-2">
			<!-- Y-axis value labels live outside the SVG on purpose: the chart below is
			     stretched non-uniformly (preserveAspectRatio="none") to fill the available
			     width, which would visibly distort any <text> drawn inside its viewBox. -->
			<div class="text-muted-foreground flex w-12 shrink-0 flex-col justify-between py-1 text-right text-[10px] tabular-nums">
				<span>{formatValue(peakValue)}</span>
				<span>{formatValue(peakValue / 2)}</span>
				<span>{formatValue(0)}</span>
			</div>

			<div class="min-w-0 flex-1">
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
									aria-label="{METRIC_HEADING[metric]}, last 30 days"
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

									<path d={areaPathFor()} fill="var(--chart-1)" fill-opacity="0.12" stroke="none" />
									<path
										d={pathFor()}
										fill="none"
										stroke="var(--chart-1)"
										stroke-width="2"
										stroke-linecap="round"
										stroke-linejoin="round"
										vector-effect="non-scaling-stroke"
									/>
								</svg>
							{/snippet}
						</Tooltip.Trigger>
						{#if hoverIndex !== null}
							<Tooltip.Content>
								<div class="flex flex-col gap-0.5">
									<span class="font-medium">{formatDay(days[hoverIndex])}</span>
									<span>{formatValue(points[hoverIndex]?.value ?? 0)}{metric === 'ingestion' ? ' that day' : ' total'}</span>
								</div>
							</Tooltip.Content>
						{/if}
					</Tooltip.Root>
				</Tooltip.Provider>

				<div class="text-muted-foreground flex justify-between text-xs">
					<span>{formatDay(days[0])}</span>
					<span>{formatDay(days[days.length - 1])}</span>
				</div>
			</div>
		</div>
	{/if}
</div>
