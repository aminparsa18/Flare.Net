<script lang="ts">
	// Hand-rolled chart - same viewBox/array-index-x/hover-tooltip technique
	// MetricChart/VolumeChart already establish (see MetricChart's own remarks for the
	// full rationale). Simpler than MetricChart: always exactly 3 lines (Logs/Traces/
	// Metrics, summed across gRPC+HTTP - protocol is a receiver-plumbing detail, not
	// something worth a 4th chart dimension), and the bucket grid is already dense
	// (BuildBuckets fills every minute), so there's no gap-handling to do.
	import * as Tooltip from '$lib/components/ui/tooltip';
	import { Spinner } from '$lib/components/ui/spinner';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatCount } from '$lib/ingestion/format';
	import type { IngestionSignal } from '$lib/ingestion-api';

	const ingestion = ingestionContext.get();

	const SIGNAL_COLOR: Record<IngestionSignal, string> = {
		Logs: 'var(--chart-1)',
		Traces: 'var(--chart-2)',
		Metrics: 'var(--chart-4)'
	};
	const SIGNALS: IngestionSignal[] = ['Logs', 'Traces', 'Metrics'];

	interface LineSpec {
		signal: IngestionSignal;
		color: string;
		points: { bucketStart: string; value: number }[];
	}

	const lines = $derived.by((): LineSpec[] => {
		const buckets = ingestion.stats?.buckets ?? [];
		return SIGNALS.map((signal) => {
			const bySignal = buckets.filter((b) => b.signal === signal);
			const byBucket = new Map<string, number>();
			for (const b of bySignal) {
				byBucket.set(b.bucketStart, (byBucket.get(b.bucketStart) ?? 0) + b.records);
			}
			const points = [...byBucket.entries()]
				.sort(([a], [b]) => a.localeCompare(b))
				.map(([bucketStart, value]) => ({ bucketStart, value }));
			return { signal, color: SIGNAL_COLOR[signal], points };
		});
	});

	const bucketStarts = $derived(lines[0]?.points.map((p) => p.bucketStart) ?? []);

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 140;
	const BASELINE_Y = CHART_HEIGHT - 4;
	const PEAK_Y = 6;

	const peakValue = $derived(Math.max(0, ...lines.flatMap((l) => l.points.map((p) => p.value))));
	const maxValue = $derived(Math.max(1e-9, peakValue));

	function xFor(index: number): number {
		const count = bucketStarts.length;
		if (count <= 1) return CHART_WIDTH / 2;
		return (index / (count - 1)) * CHART_WIDTH;
	}

	function yFor(value: number): number {
		return BASELINE_Y - (value / maxValue) * (BASELINE_Y - PEAK_Y);
	}

	function pathFor(points: { value: number }[]): string {
		return points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${xFor(i)} ${yFor(p.value)}`).join(' ');
	}

	let hoverIndex = $state<number | null>(null);

	function handlePointerMove(e: PointerEvent) {
		if (bucketStarts.length === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		const rect = svg.getBoundingClientRect();
		const fraction = (e.clientX - rect.left) / rect.width;
		hoverIndex = Math.min(bucketStarts.length - 1, Math.max(0, Math.round(fraction * (bucketStarts.length - 1))));
	}

	function formatBucketTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false, hour: '2-digit', minute: '2-digit' });
	}
</script>

<div class="flex flex-col gap-2 px-4 pb-4">
	<div class="text-muted-foreground flex items-center gap-4 text-xs">
		{#each lines as line (line.signal)}
			<span class="flex items-center gap-1.5">
				<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"></span>
				{line.signal}
			</span>
		{/each}
	</div>

	{#if ingestion.loading}
		<div class="flex h-[140px] items-center justify-center">
			<Spinner />
		</div>
	{:else if bucketStarts.length === 0}
		<div class="text-muted-foreground flex h-[140px] items-center justify-center text-xs">No data in range</div>
	{:else}
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
							aria-label="Ingested events per minute, by signal"
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

							{#each lines as line (line.signal)}
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
							<span class="font-medium">{formatBucketTime(bucketStarts[hoverIndex])}</span>
							{#each lines as line (line.signal)}
								<span class="flex items-center gap-1.5">
									<span class="inline-block h-2 w-2 shrink-0 rounded-full" style="background: {line.color};"></span>
									{line.signal}: {formatCount(line.points[hoverIndex!]?.value ?? 0)}
								</span>
							{/each}
						</div>
					</Tooltip.Content>
				{/if}
			</Tooltip.Root>
		</Tooltip.Provider>
	{/if}
</div>
