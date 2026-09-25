<script lang="ts">
	import { browser } from '$app/environment';
	import { aggregateLogs, type LogAggregateBucket } from '$lib/api';
	import type { VolumeGroupBy } from '$lib/logs/state.svelte';
	import { pickBucketWidthSeconds, formatBucketWidthSeconds } from '$lib/logs/bucket-width';
	import { resolveTimeRange, shiftRange } from '$lib/logs/time-range';
	import { logsExplorerContext } from '$lib/logs/context';
	import * as Accordion from '$lib/components/ui/accordion';
	import * as Tooltip from '$lib/components/ui/tooltip';
	import XIcon from '@lucide/svelte/icons/x';
	import * as m from '$lib/paraglide/messages';

	const explorer = logsExplorerContext.get();

	// Defaults true (the Logs Explorer page's own toolbar-driven usage) - dashboards pass
	// false here (DashboardLogsPanelBody.svelte) so a panel's chart can't silently diverge
	// its query window from the dashboard-wide time-range override; see that override's own
	// remarks and the roadmap's "one global time range driving every panel" design note.
	// Only gates the *range-mutating* drag-zoom gesture below - the harmless click-to-
	// highlight-a-bucket path (filterToBucketAt, which never touches the fetched range)
	// still works everywhere.
	let { allowZoom = true }: { allowZoom?: boolean } = $props();

	const LIVE_POLL_MS = 15_000; // not per-event recompute - a burst could mean dozens of /aggregate calls/sec for a visual that doesn't need per-event resolution
	const LIVE_TRAILING_WINDOW_MS = 60 * 60 * 1000; // matches LogFilterSqlBuilder.DefaultLookback (1h) so live mode's window feels consistent with the unfiltered-search default

	// The collapse/expand toggle is a single-item shadcn/bits-ui Accordion (see
	// $lib/components/ui/accordion) rather than a hand-rolled collapsed boolean +
	// custom animation - open/close animation, keyboard handling, and aria wiring all
	// come from the primitive itself. "" is bits-ui's own closed-accordion value for
	// type="single"; VOLUME_ITEM is this section's (only) open value.
	const VOLUME_ITEM = 'volume';
	const COLLAPSE_STORAGE_KEY = 'flare.logs.volumeChartCollapsed';

	function loadStoredValue(): string {
		if (!browser) return VOLUME_ITEM;
		try {
			return localStorage.getItem(COLLAPSE_STORAGE_KEY) === 'true' ? '' : VOLUME_ITEM;
		} catch {
			return VOLUME_ITEM; // storage disabled (e.g. private browsing) - fall back to expanded
		}
	}

	let accordionValue = $state(loadStoredValue());

	// Persists whenever the accordion opens/closes (header click or keyboard) - a layout
	// preference, not part of LogsExplorerState (which is about what's queried, not how
	// the page is laid out), same reasoning metrics/+page.svelte's pickerWidth documents
	// for its own localStorage use.
	$effect(() => {
		const value = accordionValue;
		if (!browser) return;
		try {
			localStorage.setItem(COLLAPSE_STORAGE_KEY, String(value !== VOLUME_ITEM));
		} catch {
			// Non-critical - the next reload just falls back to expanded instead.
		}
	});

	const collapsed = $derived(accordionValue !== VOLUME_ITEM);

	// Raw `/api/logs/aggregate` rows - one per bucket, or one per (bucket, group value)
	// while grouped. Everything below reads the pivoted `buckets` instead (one entry per
	// bar), so click/hover/zoom stay bar-indexed whether or not the chart is stacked.
	let rawBuckets = $state<LogAggregateBucket[]>([]);
	// The attribute the *last successful fetch* grouped by - same "as of the query, not the
	// live filter" pairing as overlayShiftSeconds below, so toggling the group-by doesn't
	// re-colour the old bars as one stack before the new data lands.
	let resultGroupBy = $state<VolumeGroupBy | null>(null);

	/** One stacked slice of a bar - `key` null is the server's rolled-up "other" series, `''` events missing the attribute. */
	type Segment = { key: string | null; count: number };
	type Bar = { bucketStart: string; count: number; segments: Segment[] };

	// Series order for the stack (bottom -> top) and legend: most events first, "other"
	// always last. Colours are assigned by this rank, not hashed like MetricChart's
	// seriesColor - the server caps an attribute group-by at 5 values
	// (LogAggregateQueryBuilder.AttributeGroupLimit), exactly the palette's 5 hues, so rank
	// guarantees adjacent stacked segments never share a colour, which a hash can't.
	const seriesKeys = $derived.by((): (string | null)[] => {
		if (!resultGroupBy) return [];
		const totals = new Map<string | null, number>();
		for (const b of rawBuckets) totals.set(b.groupKey, (totals.get(b.groupKey) ?? 0) + b.count);
		return [...totals.keys()].sort((a, b) => {
			if (a === null) return 1;
			if (b === null) return -1;
			return (totals.get(b) ?? 0) - (totals.get(a) ?? 0);
		});
	});

	const SERIES_COLOR_VARS = ['--chart-1', '--chart-2', '--chart-3', '--chart-4', '--chart-5'] as const;

	function segmentColor(key: string | null): string {
		if (key === null) return 'var(--muted-foreground)';
		const rank = seriesKeys.indexOf(key);
		return `var(${SERIES_COLOR_VARS[Math.max(0, rank) % SERIES_COLOR_VARS.length]})`;
	}

	function segmentLabel(key: string | null): string {
		if (key === null) return m.volumeChart_groupOther();
		if (key === '') return m.volumeChart_groupEmpty();
		return key;
	}

	const buckets = $derived.by((): Bar[] => {
		if (!resultGroupBy) return rawBuckets.map((b) => ({ bucketStart: b.bucketStart, count: b.count, segments: [] }));
		const byStart = new Map<number, { bucketStart: string; counts: Map<string | null, number> }>();
		for (const b of rawBuckets) {
			const t = new Date(b.bucketStart).getTime();
			let bar = byStart.get(t);
			if (!bar) byStart.set(t, (bar = { bucketStart: b.bucketStart, counts: new Map() }));
			bar.counts.set(b.groupKey, (bar.counts.get(b.groupKey) ?? 0) + b.count);
		}
		return [...byStart.entries()]
			.sort(([a], [b]) => a - b)
			.map(([, bar]) => {
				const segments = seriesKeys.filter((k) => bar.counts.has(k)).map((k) => ({ key: k, count: bar.counts.get(k)! }));
				return { bucketStart: bar.bucketStart, count: segments.reduce((sum, seg) => sum + seg.count, 0), segments };
			});
	});
	// Time-shift overlay (roadmap: "Per-query post-processing functions (metrics and
	// logs)", ADR-0042) - a second, best-effort `/api/logs/aggregate` fetch at a fixed
	// offset (explorer.filter.timeShiftSeconds), rendered as a dashed line over the bars.
	// `overlayShiftSeconds` is the offset *as of the last fetch attempt*, not the live
	// filter value - same "as of the query, not the live filter, no blink" pairing
	// MetricsExplorerState.resultTimeShiftSeconds documents, kept locally here rather than
	// on LogsExplorerState since VolumeChart's own refresh() is already the one call site
	// that talks to /api/logs/aggregate (see ADR-0041's "request-building lives in
	// VolumeChart, not LogsExplorerState" decision, which this follows for the same
	// reason).
	let overlayBuckets = $state<LogAggregateBucket[]>([]);
	let overlayShiftSeconds = $state<number | null>(null);
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
		// Only reachable if 'custom' is selected but no range has been picked yet
		// (resolveTimeRange only returns null for that case - see its own remarks).
		const to = new Date();
		return { from: new Date(to.getTime() - LIVE_TRAILING_WINDOW_MS).toISOString(), to: to.toISOString() };
	}

	async function refresh() {
		const range = currentRange();
		const rangeSeconds = (new Date(range.to).getTime() - new Date(range.from).getTime()) / 1000;
		const width = pickBucketWidthSeconds(rangeSeconds);
		const postProcessFunctions =
			explorer.filter.postProcessFunctions.length > 0 ? explorer.filter.postProcessFunctions : undefined;
		// Disabled while live: a live-tailing chart's window is a fixed trailing slice that
		// keeps sliding forward every poll, so "N hours/days ago" would itself have to keep
		// re-resolving on every tick for a comparison that doesn't mean much against a
		// window that's still filling in - same reasoning VolumeChart's drag-to-zoom already
		// falls back to a plain click while live (see handlePointerUp's own remarks).
		const shiftSeconds = explorer.live ? null : explorer.filter.timeShiftSeconds;
		const overlayRange = shiftSeconds != null ? shiftRange(range, shiftSeconds) : null;
		const groupBy = explorer.filter.volumeGroupBy;
		try {
			const [res, overlayRes] = await Promise.all([
				aggregateLogs({
					filter: explorer.buildFilter(range),
					bucketWidthSeconds: width,
					postProcessFunctions,
					...(groupBy ? { groupBy: 'Attribute' as const, groupByAttribute: { ...groupBy } } : {})
				}),
				// The overlay stays ungrouped even while the bars are stacked - it's a single
				// dashed "total, N ago" line, which is what the percent-change summary compares.
				overlayRange
					? aggregateLogs({
							filter: explorer.buildFilter(overlayRange),
							bucketWidthSeconds: width,
							postProcessFunctions
						}).catch(() => null)
					: Promise.resolve(null)
			]);
			rawBuckets = res.buckets;
			resultGroupBy = groupBy ? { ...groupBy } : null;
			overlayBuckets = overlayRes?.buckets ?? [];
			overlayShiftSeconds = overlayRes ? shiftSeconds : null;
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
		void explorer.filter.scopeNames;
		void explorer.filter.severityNumbers;
		void explorer.filter.search;
		// Everything else buildFilter reads - without these the chart stayed stale after an
		// attribute/JSON-path/pattern filter change while the log table re-searched.
		void explorer.filter.patternId;
		void explorer.filter.attribute;
		void explorer.filter.attributeFilters;
		void explorer.filter.bodyJsonFilters;
		void explorer.filter.postProcessFunctions;
		void explorer.filter.timeShiftSeconds;
		void explorer.filter.volumeGroupBy;
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

	// True only once a fetch has actually landed an overlay (see overlayShiftSeconds' own
	// remarks) - gates every overlay-specific render/scale decision below, same "keyed off
	// the result, not the live filter" reasoning MetricChart's compareActive/
	// timeShiftActive give for the identical check.
	const overlayActive = $derived(overlayShiftSeconds != null);

	// Real peak (for the y-axis labels) vs. the height-calc denominator (never 0, or every
	// bar in an all-zero window would divide by zero and render full-height). Includes the
	// overlay's own counts while active so the bars and the overlay line share one scale -
	// same reasoning MetricChart's shared y-domain gives for plotting compare/time-shift's
	// current+overlay lines together (see its own domainMin/domainMax remarks).
	const peakCount = $derived(
		Math.max(0, ...buckets.map((b) => b.count), ...(overlayActive ? overlayBuckets.map((b) => b.count) : []))
	);
	const maxCount = $derived(Math.max(1, peakCount));
	const totalCount = $derived(buckets.reduce((sum, b) => sum + b.count, 0));
	const overlayTotalCount = $derived(overlayBuckets.reduce((sum, b) => sum + b.count, 0));

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

	/**
	 * Y position for one overlay-line point - unlike barHeight, no MIN_BAR_HEIGHT floor (a
	 * line's zero should sit exactly on the baseline, not float above it) and no
	 * `count === 0` special case (the plain formula already lands exactly on BASELINE_Y for
	 * 0). Clamped into [PEAK_Y, BASELINE_Y] so a post-processed negative/oversized count
	 * (ADR-0041's own "not specially handled" consequence for bars) draws pinned to an edge
	 * of the chart instead of off it.
	 */
	function overlayY(count: number): number {
		const y = BASELINE_Y - (count / maxCount) * (BASELINE_Y - PEAK_Y);
		return Math.min(BASELINE_Y, Math.max(PEAK_Y, y));
	}

	/**
	 * Overlay points are spread evenly across the full chart width independently of the
	 * main bars' own `barWidth` (rather than reused at the same per-bucket x positions) -
	 * `overlayBuckets` can have a different length than `buckets` (real gaps differ between
	 * the two windows, or `toStartOfInterval`'s epoch-anchored grid lands a boundary
	 * differently for a shift that isn't a whole multiple of the bucket width), and this
	 * codebase's existing "array-index position, not a real time scale" simplification
	 * (see this file's own remarks on `barWidth`, and MetricChart's identical x-domain
	 * choice) already accepts that kind of imprecision rather than aligning by timestamp.
	 */
	const overlayLinePoints = $derived.by(() => {
		if (!overlayActive || overlayBuckets.length === 0) return '';
		return overlayBuckets
			.map((b, i) => `${((i + 0.5) / overlayBuckets.length) * CHART_WIDTH},${overlayY(b.count)}`)
			.join(' ');
	});

	const overlayLabel = $derived(overlayShiftSeconds != null ? formatBucketWidthSeconds(overlayShiftSeconds) : '');

	/** Mirrors MetricChart's comparePercent - null when there's nothing to compare (no
	 *  overlay, or the overlay period has no data to divide by). */
	const overlayPercent = $derived.by((): number | 'new' | null => {
		if (!overlayActive) return null;
		if (overlayTotalCount === 0) return totalCount === 0 ? null : 'new';
		return ((totalCount - overlayTotalCount) / overlayTotalCount) * 100;
	});

	const overlayChangeText = $derived.by(() => {
		if (overlayPercent === null) return null;
		const period = m.volumeChart_timeShiftAgoLabel({ duration: overlayLabel });
		if (overlayPercent === 'new') return m.volumeChart_timeShiftNew({ period });
		const sign = overlayPercent > 0 ? '+' : '';
		return m.volumeChart_timeShiftChange({ percent: `${sign}${overlayPercent.toFixed(0)}`, period });
	});

	/** Names the actual compared dates on hover - "7d ago" only names the offset, not which
	 *  dates that resolves to. Mirrors MetricChart's compareRangeDetail. */
	const overlayRangeDetail = $derived.by(() => {
		if (!overlayChangeText || !rangeFrom || !rangeTo || overlayShiftSeconds == null) return null;
		const overlay = shiftRange({ from: rangeFrom, to: rangeTo }, overlayShiftSeconds);
		const fmt = (iso: string) =>
			new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
		return `${m.volumeChart_timeShiftRangeCurrent({ from: fmt(rangeFrom), to: fmt(rangeTo) })}\n${m.volumeChart_timeShiftRangePrevious({ from: fmt(overlay.from), to: fmt(overlay.to) })}`;
	});

	const BAR_CORNER_RADIUS = 3;

	/**
	 * Stacked slices for one bar, bottom-up in `seriesKeys` order. Heights are each
	 * slice's share of the bar's own `barHeight` (so the MIN_BAR_HEIGHT floor applies to
	 * the whole bar, not per slice); negative post-processed counts contribute nothing.
	 * Only the topmost slice gets rounded corners.
	 */
	function stackedSegments(bar: Bar): { key: string | null; y: number; height: number; top: boolean }[] {
		const positive = bar.segments.filter((seg) => seg.count > 0);
		const total = positive.reduce((sum, seg) => sum + seg.count, 0);
		const full = barHeight(total);
		let y = BASELINE_Y;
		return positive.map((seg, i) => {
			const height = total === 0 ? 0 : (seg.count / total) * full;
			y -= height;
			return { key: seg.key, y, height, top: i === positive.length - 1 };
		});
	}

	/** Rounds only the top two corners, flush at the bottom - <rect rx> rounds all four,
	    which opens a visible gap at the baseline where a bar meets the axis. */
	function barPath(x: number, y: number, width: number, height: number, rounded = true): string {
		const r = rounded ? Math.max(0, Math.min(BAR_CORNER_RADIUS, width / 2, height / 2)) : 0;
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

	/** 0-1 position along the chart's width, clamped - used for the drag-to-zoom selection
	    rather than bucketIndexAt's per-bucket rounding, so the zoomed range's edges track
	    the pointer continuously instead of snapping to whole buckets while dragging. */
	function fractionAt(svg: SVGSVGElement, clientX: number): number {
		const rect = svg.getBoundingClientRect();
		return Math.min(1, Math.max(0, (clientX - rect.left) / rect.width));
	}

	/**
	 * Filters the log table to one bucket's window - "something went wrong around 10:23" ->
	 * click the spike -> see exactly what happened. Deliberately doesn't touch this chart's
	 * own fetched range (focusBucketRange leaves filter.timeRangePreset/customRange alone) -
	 * the bars stay exactly as they were, just with the clicked one highlighted, rather than
	 * re-fetching a zoomed-in, second-resolution chart of a single bar's own window. Used by
	 * handlePointerUp for a plain click (see DRAG_THRESHOLD_PX) - a real drag zooms instead
	 * (see handlePointerUp).
	 */
	function filterToBucketAt(svg: SVGSVGElement, clientX: number) {
		if (buckets.length === 0) return;
		const index = bucketIndexAt(svg, clientX);
		const bucket = buckets[index];
		if (!bucket) return;
		const from = new Date(bucket.bucketStart);
		const to = new Date(from.getTime() + bucketWidthSeconds * 1000);
		explorer.focusBucketRange({ from, to });
	}

	// Drag-to-zoom (brush-select) state. Fractions (0-1 along the chart's width), not pixel
	// or SVG-viewBox coordinates - resilient to the element resizing mid-drag and shared
	// directly between the pixel-space threshold check (getBoundingClientRect) and the
	// viewBox-space rect the selection overlay is drawn in (fraction * CHART_WIDTH).
	let isDragging = $state(false);
	let dragStartFraction = $state(0);
	let dragEndFraction = $state(0);

	// Below this many screen pixels of movement, a press-release is a click (filter to that
	// bucket), not a drag (zoom) - without a threshold, the tiniest hand tremor on what was
	// meant as a click would zoom into a near-zero-width range instead.
	const DRAG_THRESHOLD_PX = 4;

	function handlePointerMove(e: PointerEvent) {
		if (buckets.length === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		hoverIndex = bucketIndexAt(svg, e.clientX);
		if (isDragging) dragEndFraction = fractionAt(svg, e.clientX);
	}

	function handlePointerDown(e: PointerEvent) {
		if (buckets.length === 0) return;
		const svg = e.currentTarget as SVGSVGElement;
		svg.setPointerCapture(e.pointerId); // keeps delivering move/up to this element even once the pointer leaves it
		isDragging = true;
		dragStartFraction = fractionAt(svg, e.clientX);
		dragEndFraction = dragStartFraction;
	}

	/**
	 * Resolves a press-release as either a click (filter to the released-on bucket, see
	 * filterToBucketAt) or a drag (re-fetch this chart itself zoomed into the dragged range,
	 * via LogsExplorerState.setCustomRange - the same entry point the toolbar's calendar
	 * picker uses) depending on how far the pointer travelled. Unlike a bar-click's
	 * selectedBucketRange, a drag *does* change filter.timeRangePreset/customRange - the
	 * whole point is a narrower, higher-resolution chart of the dragged window, not just a
	 * highlight over the existing one.
	 */
	function handlePointerUp(e: PointerEvent) {
		if (!isDragging) return;
		isDragging = false;
		const svg = e.currentTarget as SVGSVGElement;
		svg.releasePointerCapture(e.pointerId);

		const rect = svg.getBoundingClientRect();
		const dragPx = Math.abs(dragEndFraction - dragStartFraction) * rect.width;
		// explorer.live falls back to a plain click too: setCustomRange is a no-op while live
		// (see its own remarks), but focusBucketRange (via filterToBucketAt) works even live -
		// it exits live mode itself - so a drag started during live still does *something*
		// useful rather than silently zooming into nothing. !allowZoom takes the same
		// fallback for the same reason - a real drag still highlights whatever bucket the
		// pointer was released over instead of doing nothing.
		if (!allowZoom || dragPx < DRAG_THRESHOLD_PX || !rangeFrom || !rangeTo || explorer.live) {
			filterToBucketAt(svg, e.clientX);
			return;
		}

		const fromMs = new Date(rangeFrom).getTime();
		const toMs = new Date(rangeTo).getTime();
		const spanMs = toMs - fromMs;
		const startFraction = Math.min(dragStartFraction, dragEndFraction);
		const endFraction = Math.max(dragStartFraction, dragEndFraction);
		explorer.setCustomRange({
			from: new Date(fromMs + startFraction * spanMs),
			to: new Date(fromMs + endFraction * spanMs)
		});
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

<Accordion.Root type="single" bind:value={accordionValue} class="w-full flex-col rounded-none border-0 border-b">
	<Accordion.Item value={VOLUME_ITEM} class="border-0 data-open:bg-transparent">
		<div class="flex items-center justify-between gap-2 px-4 py-3 text-xs">
			<Accordion.Trigger
				class="text-muted-foreground hover:text-foreground group/accordion-trigger relative flex w-auto flex-none items-center justify-start gap-1 border-none p-0 text-left text-xs font-normal hover:no-underline **:data-[slot=accordion-trigger-icon]:ml-0 **:data-[slot=accordion-trigger-icon]:size-3.5"
			>
				{m.volumeChart_label()}
			</Accordion.Trigger>
			{#if !collapsed}
				{#if explorer.filter.volumeGroupBy}
					<button
						type="button"
						class="text-foreground bg-accent hover:bg-accent/70 flex max-w-[20rem] items-center gap-1 rounded px-1.5 py-0.5"
						title={m.volumeChart_clearGroupBy()}
						aria-label={m.volumeChart_clearGroupBy()}
						onclick={() => explorer.setVolumeGroupBy(null)}
					>
						<span class="truncate">{m.volumeChart_groupedBy({ key: explorer.filter.volumeGroupBy.key })}</span>
						<XIcon class="size-3 shrink-0" />
					</button>
				{/if}
				{#if selectedIndex !== null && buckets[selectedIndex]}
					<button
						type="button"
						class="text-foreground bg-accent hover:bg-accent/70 flex items-center gap-1 rounded px-1.5 py-0.5"
						onclick={() => explorer.clearSelectedBucket()}
					>
						{m.volumeChart_filteredTo({ time: formatBucketTime(buckets[selectedIndex].bucketStart) })}
						<XIcon class="size-3" />
					</button>
				{/if}
				<span class="text-muted-foreground tabular-nums">{m.logs_eventsCount({ count: formatCount(totalCount) })}</span>
				{#if overlayChangeText}
					<Tooltip.Provider>
						<Tooltip.Root>
							<Tooltip.Trigger>
								{#snippet child({ props })}
									<!-- Same "hoverable, no color-coding" treatment MetricChart's own
									     compareChangeText gives its percent-change summary - see its
									     remarks on why "up" isn't judged good or bad here either. -->
									<span
										{...props}
										class="text-muted-foreground decoration-muted-foreground/50 font-medium underline decoration-dotted underline-offset-2"
									>
										{overlayChangeText}
									</span>
								{/snippet}
							</Tooltip.Trigger>
							{#if overlayRangeDetail}
								<Tooltip.Content>
									<span class="whitespace-pre-line">{overlayRangeDetail}</span>
								</Tooltip.Content>
							{/if}
						</Tooltip.Root>
					</Tooltip.Provider>
				{/if}
			{/if}
		</div>
		<Accordion.Content class="px-4 pb-3">
			{#if fetchError}
				<p class="text-destructive text-xs">{m.volumeChart_errorPrefix({ error: fetchError })}</p>
			{:else if buckets.length === 0}
				<div class="text-muted-foreground flex h-[100px] items-center justify-center text-xs">{m.logs_noData()}</div>
			{:else}
				<div class="grid grid-cols-[2.5rem_1fr] gap-x-2">
					<div class="text-muted-foreground flex h-[100px] flex-col justify-between py-0.5 text-right text-[10px] tabular-nums">
						<span>{formatCount(peakCount)}</span>
						<span>{formatCount(Math.round(peakCount / 2))}</span>
						<span>0</span>
					</div>
					<Tooltip.Provider>
						<Tooltip.Root open={hoverIndex !== null && !isDragging}>
							<Tooltip.Trigger>
								{#snippet child({ props })}
									<svg
										{...props}
										viewBox="0 0 {CHART_WIDTH} {CHART_HEIGHT}"
										preserveAspectRatio="none"
										class="h-[100px] w-full cursor-crosshair"
										role="img"
										aria-label={m.volumeChart_chartAriaLabel()}
										onpointermove={handlePointerMove}
										onpointerleave={() => {
											if (!isDragging) hoverIndex = null;
										}}
										onpointerdown={handlePointerDown}
										onpointerup={handlePointerUp}
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
											{#if resultGroupBy}
												{#each stackedSegments(bucket) as seg (seg.key)}
													<path
														d={barPath(i * barWidth + 1, seg.y, Math.max(1, barWidth - 2), seg.height, seg.top)}
														style="fill: {segmentColor(seg.key)}; fill-opacity: {hoverIndex === i || selectedIndex === i ? 1 : 0.7};"
													/>
												{/each}
												{#if selectedIndex === i}
													<!-- Outline the whole stack, not each slice, for the selected bar. -->
													<path
														d={barPath(i * barWidth + 1, BASELINE_Y - barHeight(bucket.count), Math.max(1, barWidth - 2), barHeight(bucket.count))}
														style="fill: none; stroke: var(--foreground); stroke-width: 1.5;"
														vector-effect="non-scaling-stroke"
													/>
												{/if}
											{:else}
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
											{/if}
										{/each}

										{#if overlayActive}
											<!-- Time-shift overlay (roadmap: "Per-query post-processing functions
											     (metrics and logs)", ADR-0042) - a dashed line over the bars, evenly
											     spread across the chart's full width (see overlayLinePoints' own
											     remarks on why not the bars' own barWidth-based x positions). Same
											     dashed/muted styling MetricChart's own "Previous"/time-shift overlay
											     line uses (var(--muted-foreground), stroke-dasharray). -->
											<polyline
												points={overlayLinePoints}
												fill="none"
												class="text-muted-foreground"
												stroke="currentColor"
												stroke-width="1.5"
												stroke-dasharray="4,2"
												vector-effect="non-scaling-stroke"
											/>
										{/if}

										{#if isDragging && !explorer.live && allowZoom}
											<!-- Drag-to-zoom selection overlay - width tracks the pointer live, released ->
											     handlePointerUp re-fetches this chart zoomed to the dragged window (or, below
											     DRAG_THRESHOLD_PX, falls back to the plain bucket-click filter). Hidden while
											     live (handlePointerUp always falls back to a plain click there, see its own
											     remarks) or when allowZoom is false (same reason) - a box that doesn't end up
											     zooming would mislead. -->
											<rect
												x={Math.min(dragStartFraction, dragEndFraction) * CHART_WIDTH}
												y={PEAK_Y}
												width={Math.abs(dragEndFraction - dragStartFraction) * CHART_WIDTH}
												height={BASELINE_Y - PEAK_Y}
												style="fill: var(--foreground); fill-opacity: 0.12; stroke: var(--foreground); stroke-opacity: 0.4;"
												stroke-width="1"
												vector-effect="non-scaling-stroke"
											/>
										{/if}
									</svg>
								{/snippet}
							</Tooltip.Trigger>
							{#if hoverIndex !== null && !isDragging && buckets[hoverIndex]}
								<Tooltip.Content>
									<span class="whitespace-pre-line">
										{m.volumeChart_tooltip({
											time: formatBucketTime(buckets[hoverIndex].bucketStart),
											count: formatCount(buckets[hoverIndex].count)
										})}
										<!-- Same index-based approximation overlayLinePoints itself uses -
										     overlayBuckets can be a different length than buckets, so this is
										     "whatever's at the same relative position", not a guaranteed exact
										     time match. -->
										<!-- Top of the stack first, matching how the bar reads. -->
										{#if resultGroupBy}
											{#each [...buckets[hoverIndex].segments].reverse() as seg (seg.key)}
												{'\n'}{m.volumeChart_tooltipSegment({ label: segmentLabel(seg.key), count: formatCount(seg.count) })}
											{/each}
										{/if}
										{#if overlayActive && overlayBuckets[hoverIndex]}
											{'\n'}{m.volumeChart_tooltipOverlay({
												count: formatCount(overlayBuckets[hoverIndex].count),
												period: m.volumeChart_timeShiftAgoLabel({ duration: overlayLabel })
											})}
										{/if}
									</span>
								</Tooltip.Content>
							{/if}
						</Tooltip.Root>
					</Tooltip.Provider>

					<div></div>
					<div class="text-muted-foreground mt-1 flex justify-between text-[10px]">
						<span>{rangeFrom ? formatAxisTime(rangeFrom) : ''}</span>
						<span>{rangeTo ? formatAxisTime(rangeTo) : ''}</span>
					</div>
					{#if resultGroupBy && seriesKeys.length > 0}
						<div></div>
						<ul class="text-muted-foreground mt-1.5 flex flex-wrap gap-x-3 gap-y-1 text-[11px]">
							{#each seriesKeys as key (key)}
								<li class="flex max-w-[16rem] items-center gap-1.5">
									<span class="size-2 shrink-0 rounded-sm" style="background: {segmentColor(key)};"></span>
									<span class="truncate font-mono" title={segmentLabel(key)}>{segmentLabel(key)}</span>
								</li>
							{/each}
						</ul>
					{/if}
				</div>
			{/if}
		</Accordion.Content>
	</Accordion.Item>
</Accordion.Root>
