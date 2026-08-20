<script lang="ts">
	import { browser } from '$app/environment';
	import { aggregateLogs, type LogAggregateBucket } from '$lib/api';
	import { pickBucketWidthSeconds } from '$lib/logs/bucket-width';
	import { resolveTimeRange } from '$lib/logs/time-range';
	import { logsExplorerContext } from '$lib/logs/context';
	import * as Tooltip from '$lib/components/ui/tooltip';
	import XIcon from '@lucide/svelte/icons/x';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';

	const explorer = logsExplorerContext.get();

	const LIVE_POLL_MS = 15_000; // not per-event recompute - a burst could mean dozens of /aggregate calls/sec for a visual that doesn't need per-event resolution
	const LIVE_TRAILING_WINDOW_MS = 60 * 60 * 1000; // matches LogFilterSqlBuilder.DefaultLookback (1h) so live mode's window feels consistent with the unfiltered-search default

	// Collapse toggle - lets the user hide the chart to focus on the log table below,
	// same "layout preference, not query state" reasoning metrics/+page.svelte's
	// pickerWidth documents, so this lives as component-local state rather than on
	// LogsExplorerState (which is about what's queried, not how the page is laid out).
	// Persisted to localStorage so the choice survives reloads, same convention.
	const COLLAPSE_STORAGE_KEY = 'flare.logs.volumeChartCollapsed';

	function loadStoredCollapsed(): boolean {
		if (!browser) return false;
		try {
			return localStorage.getItem(COLLAPSE_STORAGE_KEY) === 'true';
		} catch {
			return false; // storage disabled (e.g. private browsing) - fall back to expanded
		}
	}

	let collapsed = $state(loadStoredCollapsed());

	function toggleCollapsed(): void {
		collapsed = !collapsed;
		try {
			localStorage.setItem(COLLAPSE_STORAGE_KEY, String(collapsed));
		} catch {
			// Non-critical - the next reload just falls back to expanded instead.
		}
	}

	let buckets = $state<LogAggregateBucket[]>([]);
	let fetchError = $state<string | null>(null);
	let hoverIndex = $state<number | null>(null);
	// The requested window, not derived from bucket boundaries - buckets can undershoot the
	// edges (e.g. a trailing partial bucket), so the axis labels use what was actually asked for.
	let rangeFrom = $state<string | null>(null);
	let rangeTo = $state<string | null>(null);
	// The width actually requested for the current `buckets` - needed to turn a clicked
	// bucket's bucketStart into a [from, to) window, since LogAggregateBucket carries no
	// bucketEnd of its own.
	let bucketWidthSeconds = $state(1);

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
		const width = pickBucketWidthSeconds(rangeSeconds);
		try {
			const res = await aggregateLogs({
				filter: explorer.buildFilter(range),
				bucketWidthSeconds: width
			});
			buckets = res.buckets;
			rangeFrom = range.from;
			rangeTo = range.to;
			bucketWidthSeconds = width;
			fetchError = null;
		} catch (err) {
			fetchError = err instanceof Error ? err.message : String(err);
		}
	}

	// Debounced refetch on any relevant filter change - $effect is warranted here (syncing
	// with the external API), unlike VirtualList's scroll handling.
	//
	// Bails before reading the filter fields while collapsed, so a hidden chart doesn't
	// keep firing /aggregate calls for a visual nobody can see - the effect's reactive
	// dependency set is only what it actually reads, so while collapsed this depends on
	// `collapsed` alone (not the filter fields below it) and simply doesn't re-run until
	// re-expanded. Re-expanding (collapsed -> false) reruns it, which reads the *current*
	// filter state and refreshes - so the chart never shows stale data from before it was
	// collapsed, it just skips redundant fetches while hidden.
	$effect(() => {
		if (collapsed) return;
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

	// Extra trailing-window poll while live, independent of the debounce above. Same
	// collapsed short-circuit as above - no point polling a hidden chart.
	$effect(() => {
		if (collapsed || !explorer.live) return;
		const interval = setInterval(refresh, LIVE_POLL_MS);
		return () => clearInterval(interval);
	});

	// Which of the *currently rendered* bars (if any) matches the explorer's active bar-click
	// selection - deliberately not part of the refetch effect above, so selecting a bucket
	// only changes this highlight, never the chart's own fetched window/resolution. Compared
	// by timestamp rather than string equality: `selectedBucketRange.from` round-trips through
	// `Date#toISOString()`, which normalizes to millisecond precision and may not equal the
	// server's own bucketStart formatting byte-for-byte.
	const selectedIndex = $derived.by(() => {
		const selection = explorer.selectedBucketRange;
		if (!selection) return null;
		const target = new Date(selection.from).getTime();
		const index = buckets.findIndex((b) => new Date(b.bucketStart).getTime() === target);
		return index === -1 ? null : index;
	});

	// Real peak (for the y-axis labels) vs. the height-calc denominator (never 0, or every
	// bar in an all-zero window would divide by zero and render full-height).
	const peakCount = $derived(Math.max(0, ...buckets.map((b) => b.count)));
	const maxCount = $derived(Math.max(1, peakCount));
	const totalCount = $derived(buckets.reduce((sum, b) => sum + b.count, 0));

	const CHART_WIDTH = 800;
	const CHART_HEIGHT = 100;
	const BASELINE_Y = CHART_HEIGHT - 2;
	const PEAK_Y = 3;
	const MIN_BAR_HEIGHT = 2; // keeps a lone/rare event visible instead of a 0px sliver
	const barWidth = $derived(CHART_WIDTH / Math.max(1, buckets.length));

	function barHeight(count: number): number {
		if (count === 0) return 0;
		return Math.max(MIN_BAR_HEIGHT, (count / maxCount) * (BASELINE_Y - PEAK_Y));
	}

	const BAR_CORNER_RADIUS = 3;

	/** Rounds only the top two corners, flush at the bottom - <rect rx> rounds all four,
	    which opens a visible gap at the baseline where a bar meets the axis. */
	function barPath(x: number, y: number, width: number, height: number): string {
		const r = Math.max(0, Math.min(BAR_CORNER_RADIUS, width / 2, height / 2));
		if (r === 0) {
			return `M ${x} ${y + height} L ${x} ${y} L ${x + width} ${y} L ${x + width} ${y + height} Z`;
		}
		return `M ${x} ${y + height}
			L ${x} ${y + r}
			A ${r} ${r} 0 0 1 ${x + r} ${y}
			L ${x + width - r} ${y}
			A ${r} ${r} 0 0 1 ${x + width} ${y + r}
			L ${x + width} ${y + height}
			Z`;
	}

	/** Shared by hover and click - maps a pointer's x position to a bucket index. */
	function bucketIndexAt(svg: SVGSVGElement, clientX: number): number {
		const rect = svg.getBoundingClientRect();
		const fraction = (clientX - rect.left) / rect.width;
		return Math.min(buckets.length - 1, Math.max(0, Math.floor(fraction * buckets.length)));
	}

	function handlePointerMove(e: PointerEvent) {
		if (buckets.length === 0) return;
		hoverIndex = bucketIndexAt(e.currentTarget as SVGSVGElement, e.clientX);
	}

	/**
	 * Clicking a bar (or anywhere along its column) filters the log table to that bucket's
	 * window - "something went wrong around 10:23" -> click the spike -> see exactly what
	 * happened. Deliberately doesn't touch this chart's own fetched range (focusBucketRange
	 * leaves filter.timeRangePreset/customRange alone) - the bars stay exactly as they were,
	 * just with the clicked one highlighted, rather than re-fetching a zoomed-in,
	 * second-resolution chart of a single bar's own window.
	 */
	function handleBarClick(e: MouseEvent) {
		if (buckets.length === 0) return;
		const index = bucketIndexAt(e.currentTarget as SVGSVGElement, e.clientX);
		const bucket = buckets[index];
		if (!bucket) return;
		const from = new Date(bucket.bucketStart);
		const to = new Date(from.getTime() + bucketWidthSeconds * 1000);
		explorer.focusBucketRange({ from, to });
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

	function formatAxisTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false, hour: '2-digit', minute: '2-digit' });
	}

	const compactCount = new Intl.NumberFormat(undefined, { notation: 'compact' });
	function formatCount(n: number): string {
		return compactCount.format(n);
	}
</script>

<div class="border-b px-4 {collapsed ? 'py-1.5' : 'py-3'}">
	<div class="text-muted-foreground flex items-center justify-between text-xs" class:mb-1={!collapsed}>
		<button
			type="button"
			class="hover:text-foreground -ml-1 flex items-center gap-1 rounded px-1 py-0.5"
			aria-expanded={!collapsed}
			onclick={toggleCollapsed}
		>
			<ChevronDownIcon class="size-3.5 transition-transform {collapsed ? '-rotate-90' : ''}" />
			Event volume
		</button>
		{#if !collapsed}
			{#if selectedIndex !== null && buckets[selectedIndex]}
				<button
					type="button"
					class="text-foreground bg-accent hover:bg-accent/70 flex items-center gap-1 rounded px-1.5 py-0.5"
					onclick={() => explorer.clearSelectedBucket()}
				>
					Filtered to {formatBucketTime(buckets[selectedIndex].bucketStart)}
					<XIcon class="size-3" />
				</button>
			{/if}
			<span class="tabular-nums">{formatCount(totalCount)} events</span>
		{/if}
	</div>
	{#if !collapsed}
		{#if fetchError}
			<p class="text-destructive text-xs">Volume chart: {fetchError}</p>
		{:else if buckets.length === 0}
			<div class="text-muted-foreground flex h-[100px] items-center justify-center text-xs">No data</div>
		{:else}
			<div class="grid grid-cols-[2.5rem_1fr] gap-x-2">
				<div class="text-muted-foreground flex h-[100px] flex-col justify-between py-0.5 text-right text-[10px] tabular-nums">
					<span>{formatCount(peakCount)}</span>
					<span>{formatCount(Math.round(peakCount / 2))}</span>
					<span>0</span>
				</div>
				<Tooltip.Provider>
					<Tooltip.Root open={hoverIndex !== null}>
						<Tooltip.Trigger>
							{#snippet child({ props })}
								<svg
									{...props}
									viewBox="0 0 {CHART_WIDTH} {CHART_HEIGHT}"
									preserveAspectRatio="none"
									class="h-[100px] w-full cursor-pointer"
									role="img"
									aria-label="Event volume over time - click a bar to filter logs to that time range"
									onpointermove={handlePointerMove}
									onpointerleave={() => (hoverIndex = null)}
									onclick={handleBarClick}
								>
									<!-- Gridlines at peak / half / zero, aligned with the y-axis labels beside them.
									     non-scaling-stroke keeps them a crisp 1px regardless of the viewBox's
									     non-uniform stretch (preserveAspectRatio="none" scales x and y independently). -->
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

									{#if selectedIndex !== null}
										<!-- Persistent (not hover-only) marker for the bar-click selection, so the
										     filtered-to range stays visible even after the pointer moves away. -->
										<line
											x1={selectedIndex * barWidth + barWidth / 2}
											y1={PEAK_Y}
											x2={selectedIndex * barWidth + barWidth / 2}
											y2={BASELINE_Y}
											class="text-foreground"
											stroke="currentColor"
											stroke-width="1"
											vector-effect="non-scaling-stroke"
										/>
									{/if}

									{#if hoverIndex !== null && hoverIndex !== selectedIndex}
										<line
											x1={hoverIndex * barWidth + barWidth / 2}
											y1={PEAK_Y}
											x2={hoverIndex * barWidth + barWidth / 2}
											y2={BASELINE_Y}
											class="text-muted-foreground"
											stroke="currentColor"
											stroke-width="1"
											stroke-dasharray="2,2"
											vector-effect="non-scaling-stroke"
										/>
									{/if}

									{#each buckets as bucket, i (bucket.bucketStart)}
										<!-- Inline style, not a fill-primary Tailwind class: this project's Tailwind build
										     never emits fill-*/stroke-* color utilities (confirmed - no such rule exists in
										     any stylesheet even for a plain fill-primary). --color-primary (the @theme
										     inline token) isn't a real runtime custom property either - `inline` tells
										     Tailwind to bake var(--primary) directly into generated utilities instead of
										     emitting a --color-primary custom property on :root, confirmed empty via
										     getComputedStyle. --primary itself (layout.css's own :root/.dark block) is the
										     real runtime variable, so that's what this binds to directly. -->
										<path
											d={barPath(
												i * barWidth + 1,
												BASELINE_Y - barHeight(bucket.count),
												Math.max(1, barWidth - 2),
												barHeight(bucket.count)
											)}
											style="fill: var(--primary); fill-opacity: {hoverIndex === i || selectedIndex === i
												? 1
												: 0.55}; {selectedIndex === i ? 'stroke: var(--foreground); stroke-width: 1.5;' : ''}"
											vector-effect={selectedIndex === i ? 'non-scaling-stroke' : undefined}
										/>
									{/each}
								</svg>
							{/snippet}
						</Tooltip.Trigger>
						{#if hoverIndex !== null && buckets[hoverIndex]}
							<Tooltip.Content>
								{formatBucketTime(buckets[hoverIndex].bucketStart)} · {formatCount(buckets[hoverIndex].count)} events · click to filter
							</Tooltip.Content>
						{/if}
					</Tooltip.Root>
				</Tooltip.Provider>

				<div></div>
				<div class="text-muted-foreground mt-1 flex justify-between text-[10px]">
					<span>{rangeFrom ? formatAxisTime(rangeFrom) : ''}</span>
					<span>{rangeTo ? formatAxisTime(rangeTo) : ''}</span>
				</div>
			</div>
		{/if}
	{/if}
</div>
