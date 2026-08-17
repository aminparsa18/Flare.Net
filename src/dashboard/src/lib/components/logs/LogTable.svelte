<script lang="ts">
	import { createVirtualizer } from '@tanstack/svelte-virtual';
	import LogRow from './LogRow.svelte';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import { logsExplorerContext } from '$lib/logs/context';

	const explorer = logsExplorerContext.get();

	const ROW_HEIGHT = 32;
	// Shared between the header and every LogRow via CSS custom properties (set once
	// here, per svelte-best-practices' style:--prop guidance) so the two can never drift
	// out of alignment the way two hand-copied grid-template-columns strings could.
	const COLUMNS = '170px 90px 160px 1fr'; // 170px fits the Time column's fixed "MM-DD HH:mm:ss.SSS" width

	// How many rows from the end of the currently-loaded list the last rendered row can
	// be before triggering loadMore() - mirrors the old VirtualList's pixel-based
	// endReachedThreshold, just expressed in rows since @tanstack/svelte-virtual's
	// virtual items are index-based rather than scroll-offset-based.
	const END_REACHED_LOOKAHEAD = 10;

	let scrollEl = $state<HTMLDivElement | null>(null);

	// @tanstack/svelte-virtual replaces the hand-rolled VirtualList component here (that
	// component's own fixed-itemHeight scrollTop math was the suspected source of rows
	// vanishing on append/scroll-up - see PR discussion) - VirtualList itself is left in
	// place for TraceList/VolumeChart, which aren't affected by this bug and weren't
	// asked to migrate.
	const virtualizer = createVirtualizer({
		count: explorer.events.length,
		getScrollElement: () => scrollEl,
		estimateSize: () => ROW_HEIGHT,
		overscan: 8,
		getItemKey: (index) => explorer.events[index]?.eventId ?? index
	});

	// `count`/`getItemKey` close over `explorer.events` as of virtualizer creation -
	// every subsequent change (live prepend, loadMore append, filter reset) has to be
	// pushed back in via setOptions, per @tanstack/svelte-virtual's own store contract
	// (see node_modules/@tanstack/svelte-virtual's setOptions remarks on forcing a
	// store update even when the visible range doesn't change).
	$effect(() => {
		$virtualizer.setOptions({
			count: explorer.events.length,
			getScrollElement: () => scrollEl,
			estimateSize: () => ROW_HEIGHT,
			overscan: 8,
			getItemKey: (index) => explorer.events[index]?.eventId ?? index
		});
	});

	const virtualItems = $derived($virtualizer.getVirtualItems());

	// Infinite-scroll trigger - fires (repeatedly, while still within the lookahead)
	// once the last rendered row nears the end of what's loaded so far. loadMore()
	// itself guards on nextCursor/loadingMore/live, same contract the old VirtualList's
	// onEndReached documented.
	$effect(() => {
		const last = virtualItems[virtualItems.length - 1];
		if (!last) return;
		if (last.index >= explorer.events.length - 1 - END_REACHED_LOOKAHEAD) {
			void explorer.loadMore();
		}
	});
</script>

<div
	class="flex min-h-0 flex-1 flex-col"
	style:--log-row-columns={COLUMNS}
	style:--log-row-height="{ROW_HEIGHT}px"
>
	<!-- overflow-y: hidden + scrollbar-gutter: stable reserves the same width the scroll
	     container's actual scrollbar eats into below - without it, this header (never
	     itself scrollable) would be a few px wider than the rows once there's enough data
	     to scroll, throwing the rightmost column (Message) out of alignment even with
	     gap-3 matching. -->
	<div
		class="bg-muted/30 text-muted-foreground grid shrink-0 items-center gap-3 overflow-y-hidden border-b px-3 text-xs font-medium"
		style="grid-template-columns: var(--log-row-columns); height: 28px; scrollbar-gutter: stable;"
	>
		<span>Time</span>
		<span>Level</span>
		<span>Service</span>
		<span>Message</span>
	</div>

	{#if explorer.events.length === 0 && !explorer.loading}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>No events</Empty.Title>
				<Empty.Description>
					{explorer.live ? 'Waiting for live events…' : 'No events match the current filters.'}
				</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div bind:this={scrollEl} class="min-h-0 flex-1 overflow-y-auto" style="scrollbar-gutter: stable;">
			<div style="height: {$virtualizer.getTotalSize()}px; position: relative;">
				{#each virtualItems as row (row.key)}
					{@const event = explorer.events[row.index]}
					{#if event}
						<div
							style="position: absolute; top: 0; left: 0; width: 100%; height: {row.size}px; transform: translateY({row.start}px);"
						>
							<LogRow {event} onSelect={(e) => (explorer.selectedEventId = e.eventId)} />
						</div>
					{/if}
				{/each}
			</div>
		</div>
		{#if explorer.loadingMore}
			<div class="flex shrink-0 items-center justify-center border-t py-2">
				<Spinner />
			</div>
		{/if}
	{/if}
</div>