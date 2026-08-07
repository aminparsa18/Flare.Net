<script lang="ts">
	import { aggregateLogs, type LogAggregateBucket } from '$lib/api';
	import { pickBucketWidthSeconds } from '$lib/logs/bucket-width';
	import { resolveTimeRange } from '$lib/logs/time-range';
	import { logsExplorerContext } from '$lib/logs/context';
	import * as Tooltip from '$lib/components/ui/tooltip';

	const explorer = logsExplorerContext.get();

	const LIVE_POLL_MS = 15_000; // not per-event recompute - a burst could mean dozens of /aggregate calls/sec for a visual that doesn't need per-event resolution
	const LIVE_TRAILING_WINDOW_MS = 60 * 60 * 1000; // matches LogFilterSqlBuilder.DefaultLookback (1h) so live mode's window feels consistent with the unfiltered-search default

	let buckets = $state<LogAggregateBucket[]>([]);
	let fetchError = $state<string | null>(null);
	let hoverIndex = $state<number | null>(null);

	function currentRange(): { from: string; to: string } {
		if (explorer.live) {
			const to = new Date();
			return { from: new Date(to.getTime() - LIVE_TRAILING_WINDOW_MS).toISOString(), to: to.toISOString() };
		}
		const range = resolveTimeRange(explorer.filter.timeRangePreset, explorer.filter.customRange ?? undefined);
		if (range) return range;
		// Only reachable if 'custom' is selected but no range has been picked yet.
		const to = new Date();
		return { from: new Date(to.getTime() - LIVE_TRAILING_WINDOW_MS).toISOString(), to: to.toISOString() };
	}

	async function refresh() {
		const range = currentRange();
		const rangeSeconds = (new Date(range.to).getTime() - new Date(range.from).getTime()) / 1000;
		try {
			const res = await aggregateLogs({
				filter: explorer.buildFilter(range),
				bucketWidthSeconds: pickBucketWidthSeconds(rangeSeconds)
			});
			buckets = res.buckets;
			fetchError = null;
		} catch (err) {
			fetchError = err instanceof Error ? err.message : String(err);
		}
	}

	// Debounced refetch on any relevant filter change - $effect is warranted here (syncing
	// with the external API), unlike VirtualList's scroll handling.
	$effect(() => {
		// Reading these makes the effect re-run when any of them change.
		void explorer.filter.timeRangePreset;
		void explorer.filter.customRange;
		void explorer.filter.services;
		void explorer.filter.severityNumbers;
		void explorer.filter.search;
		void explorer.live;

		const timer = setTimeout(refresh, 300);
		return () => clearTimeout(timer);
	});

	// Extra trailing-window poll while live, independent of the debounce above.
	$effect(() => {
		if (!explorer.live) return;
		const interval = setInterval(refresh, LIVE_POLL_MS);
		return () => clearInterval(interval);
	});

	const maxCount = $derived(Math.max(1, ...buckets.map((b) => b.count)));

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 100;
	const barWidth = $derived(CHART_WIDTH / Math.max(1, buckets.length));

	function barHeight(count: number): number {
		return (count / maxCount) * (CHART_HEIGHT - 4);
	}

	function handlePointerMove(e: PointerEvent) {
		if (buckets.length === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		const rect = svg.getBoundingClientRect();
		const fraction = (e.clientX - rect.left) / rect.width;
		hoverIndex = Math.min(buckets.length - 1, Math.max(0, Math.floor(fraction * buckets.length)));
	}

	function formatBucketTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, {
			hour12: false,
			month: 'short',
			day: 'numeric',
			hour: '2-digit',
			minute: '2-digit'
		});
	}
</script>

<div class="border-b px-4 py-2">
	{#if fetchError}
		<p class="text-destructive text-xs">Volume chart: {fetchError}</p>
	{:else if buckets.length === 0}
		<div class="text-muted-foreground flex h-[100px] items-center justify-center text-xs">No data</div>
	{:else}
		<Tooltip.Provider>
			<Tooltip.Root open={hoverIndex !== null}>
				<Tooltip.Trigger>
					{#snippet child({ props })}
						<svg
							{...props}
							viewBox="0 0 {CHART_WIDTH} {CHART_HEIGHT}"
							preserveAspectRatio="none"
							class="h-[100px] w-full"
							role="img"
							aria-label="Event volume over time"
							onpointermove={handlePointerMove}
							onpointerleave={() => (hoverIndex = null)}
						>
							{#each buckets as bucket, i (bucket.bucketStart)}
								<!-- Inline style, not a fill-primary Tailwind class: this project's Tailwind build
								     never emits fill-*/stroke-* color utilities (confirmed - no such rule exists in
								     any stylesheet even for a plain fill-primary). --color-primary (the @theme
								     inline token) isn't a real runtime custom property either - `inline` tells
								     Tailwind to bake var(--primary) directly into generated utilities instead of
								     emitting a --color-primary custom property on :root, confirmed empty via
								     getComputedStyle. --primary itself (layout.css's own :root/.dark block) is the
								     real runtime variable, so that's what this binds to directly. -->
								<rect
									x={i * barWidth + 1}
									y={CHART_HEIGHT - barHeight(bucket.count)}
									width={Math.max(1, barWidth - 2)}
									height={Math.max(0, barHeight(bucket.count))}
									style="fill: var(--primary); fill-opacity: {hoverIndex === i ? 1 : 0.6};"
								/>
							{/each}
						</svg>
					{/snippet}
				</Tooltip.Trigger>
				{#if hoverIndex !== null && buckets[hoverIndex]}
					<Tooltip.Content>
						{formatBucketTime(buckets[hoverIndex].bucketStart)} · {buckets[hoverIndex].count} events
					</Tooltip.Content>
				{/if}
			</Tooltip.Root>
		</Tooltip.Provider>
	{/if}
</div>
