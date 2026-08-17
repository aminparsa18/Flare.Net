<script lang="ts" module>
	import type { Snippet } from 'svelte';

	export interface VirtualListProps<T> {
		items: T[];
		/** Fixed row height in px - this list assumes uniform row height (dense log rows), not variable/measured. */
		itemHeight: number;
		/** Per svelte-best-practices: keyed each blocks must use a real identity, not the index (which shifts on every live-tail prepend). */
		getKey: (item: T, index: number) => string | number;
		overscan?: number;
		/** Fires (repeatedly, while still within threshold) once scroll nears the bottom - callers debounce/guard on their own loading state. */
		onEndReached?: () => void;
		endReachedThreshold?: number;
		children: Snippet<[item: T, index: number]>;
		class?: string;
	}
</script>

<script lang="ts" generics="T">
	import { cn } from '$lib/utils';

	let {
		items,
		itemHeight,
		getKey,
		overscan = 8,
		onEndReached,
		endReachedThreshold = 200,
		children,
		class: className
	}: VirtualListProps<T> = $props();

	let containerEl = $state<HTMLDivElement | null>(null);
	let scrollTop = $state(0);
	let containerHeight = $state(0);

	const totalHeight = $derived(items.length * itemHeight);
	const visibleCount = $derived(Math.ceil(containerHeight / itemHeight) + overscan * 2);
	// Clamped against `items.length` (via maxStartIndex), not just against 0 - `items` can
	// *shrink* out from under a stale `scrollTop` (disabling live tail swaps a large
	// live-tail buffer for a fresh, shorter search page; a filter change narrows the result
	// set) without this component finding out via a scroll event. Unclamped, startIndex
	// could land past the new items.length, making endIndex < startIndex and
	// `items.slice(startIndex, endIndex)` silently return [] - the whole list appearing to
	// vanish even though valid rows exist.
	const maxStartIndex = $derived(Math.max(0, items.length - visibleCount));
	const startIndex = $derived(
		Math.max(0, Math.min(maxStartIndex, Math.floor(scrollTop / itemHeight) - overscan))
	);
	const endIndex = $derived(Math.min(items.length, startIndex + visibleCount));
	const visibleItems = $derived(items.slice(startIndex, endIndex));
	const offsetY = $derived(startIndex * itemHeight);

	// Plain event attribute for the scroll listener (direct user-interaction handling),
	// not $effect+addEventListener - $effect is reserved below for the ResizeObserver,
	// which is genuinely "observing something external to Svelte."
	function handleScroll(e: Event) {
		const el = e.currentTarget as HTMLDivElement;
		scrollTop = el.scrollTop;
		if (onEndReached && el.scrollHeight - el.scrollTop - el.clientHeight < endReachedThreshold) {
			onEndReached();
		}
	}

	$effect(() => {
		if (!containerEl) return;
		containerHeight = containerEl.clientHeight; // avoid a 0-height flash before the first observer callback
		const observer = new ResizeObserver((entries) => {
			containerHeight = entries[0].contentRect.height;
		});
		observer.observe(containerEl);
		return () => observer.disconnect();
	});

	// Scroll-position compensation for content shifting at the front - both directions:
	// live-tail *prepending* newer rows ahead of index 0 while scrolled away from the top,
	// and a pagination cap *evicting* rows from the front once accumulated history gets
	// large (see LogsExplorerState's PAGINATION_CAP/LIVE_CAP - both trim from whichever end
	// isn't where growth is currently happening). `scrollTop` is a plain pixel offset with
	// no idea *why* `items` changed - either direction silently shifts every already-
	// rendered row's index, which (uncompensated) reads as the visible window jumping to
	// unrelated content. `previousItems` is a plain closure variable, not $state - it only
	// needs to be read/written from inside this one effect, never trigger reactivity itself.
	//
	// Deliberately *not* a items.length delta: once a cap is in effect (LIVE_CAP or
	// PAGINATION_CAP), the array stops growing/shrinking at all once steady-state is
	// reached (one row evicted for every one added), even though content keeps shifting by
	// one every update - a length-delta check would silently stop compensating right at the
	// point ("the list gets long") where the jump is most noticeable. A bounded scan for
	// where the previously-topmost row now sits (or, for eviction, where the new topmost
	// row previously sat) catches that steady-state case in both directions.
	// Must safely exceed the largest single-update batch either direction can produce -
	// live-tail prepends land close to one at a time, but a PAGINATION_CAP eviction can
	// remove up to a full page's worth (Flare.Api's LogSearchQueryBuilder.MaxPageSize is
	// 1000; LogsExplorerState's own PAGE_SIZE is 100) from the front in a single loadMore()
	// call. Sized to comfortably cover that, not just today's PAGE_SIZE - cheap even so,
	// since this scan runs at most once per items-array change, not per frame.
	const SHIFT_SCAN_LIMIT = 1024;
	// Starts undefined rather than reading `items` here (which would only capture its
	// initial value, outside any reactive context) - the effect below sets it on every run,
	// including the first, so the "skip on first run" check is `prev === undefined`, not a
	// stale initial snapshot.
	let previousItems: T[] | undefined;
	$effect(() => {
		const prev = previousItems;
		previousItems = items;
		if (!containerEl || prev === undefined || items === prev || prev.length === 0) return;
		if (containerEl.scrollTop === 0) return; // already pinned to the newest row - let content push in naturally

		// Grew from the front (a prepend): find where the old topmost row (prev[0]) now
		// sits in `items`. Found at a positive index -> shift the view down to compensate.
		let growShift = -1;
		for (let i = 0; i < Math.min(items.length, SHIFT_SCAN_LIMIT); i++) {
			if (getKey(items[i], i) === getKey(prev[0], 0)) {
				growShift = i;
				break;
			}
		}
		if (growShift > 0) {
			containerEl.scrollTop += growShift * itemHeight;
			scrollTop = containerEl.scrollTop;
			return;
		}
		if (growShift === 0) return; // prev[0] still at index 0 - a plain append, nothing to compensate

		// Not found growing forward - check the other direction: shrank from the front (an
		// eviction). Find where the new topmost row (items[0]) previously sat in `prev`;
		// found at a positive index -> shift the view up by however many rows were trimmed.
		let shrinkShift = -1;
		if (items.length > 0) {
			for (let i = 0; i < Math.min(prev.length, SHIFT_SCAN_LIMIT); i++) {
				if (getKey(prev[i], i) === getKey(items[0], 0)) {
					shrinkShift = i;
					break;
				}
			}
		}
		if (shrinkShift > 0) {
			containerEl.scrollTop = Math.max(0, containerEl.scrollTop - shrinkShift * itemHeight);
			scrollTop = containerEl.scrollTop;
		}
		// else not found in either direction within the scan window - a wholesale replace
		// (filter change/disabling live), deliberately left alone here; this component's own
		// clamp above already keeps that case from rendering as an empty slice instead.
	});
</script>

<!-- scrollbar-gutter: stable keeps this container's content width constant whether or not
     the list is currently tall enough to actually scroll - LogTable's header (never itself
     scrollable) reserves the same gutter via the same property, so columns stay aligned
     whether or not a scrollbar is visible. -->
<div
	bind:this={containerEl}
	class={cn('relative overflow-y-auto', className)}
	style="scrollbar-gutter: stable;"
	onscroll={handleScroll}
>
	<div style="height: {totalHeight}px; position: relative;">
		<div style="transform: translateY({offsetY}px);">
			{#each visibleItems as item, i (getKey(item, startIndex + i))}
				{@render children(item, startIndex + i)}
			{/each}
		</div>
	</div>
</div>
