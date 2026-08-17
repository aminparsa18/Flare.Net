<script lang="ts">
	import SvelteVirtualList from '@humanspeak/svelte-virtual-list';
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
</script>

<div
	class="flex min-h-0 flex-1 flex-col"
	style:--log-row-columns={COLUMNS}
	style:--log-row-height="{ROW_HEIGHT}px"
>
	<!-- overflow-y: hidden + scrollbar-gutter: stable reserves the same width the list's own
	     scrollbar (always-on - see below) eats into - without it, this header (never itself
	     scrollable) would be a few px wider than the rows once there's enough data to scroll,
	     throwing the rightmost column (Message) out of alignment even with gap-3 matching. -->
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
		<!-- @humanspeak/svelte-virtual-list replaces the hand-rolled VirtualList component
		     here (that component's own fixed-itemHeight scrollTop math had no anchor
		     preservation for live-tail prepends - see its remarks) - VirtualList itself is
		     left in place unused, not deleted, per the ask. This wrapper is what gives the
		     library's own container (height: 100%, untouched default class) something to
		     fill in LogTable's flex column - containerClass/viewportClass aren't passed
		     here since supplying either replaces its built-in positioning/overflow CSS
		     entirely rather than merging with it. -->
		<div class="min-h-0 flex-1">
			<SvelteVirtualList
				items={explorer.events}
				itemKey={(event) => event.eventId}
				defaultEstimatedItemSize={ROW_HEIGHT}
				onLoadMore={() => void explorer.loadMore()}
				hasMore={!!explorer.nextCursor}
			>
				{#snippet renderItem(event)}
					<LogRow {event} onSelect={(e) => (explorer.selectedEventId = e.eventId)} />
				{/snippet}
			</SvelteVirtualList>
		</div>
		{#if explorer.loadingMore}
			<div class="flex shrink-0 items-center justify-center border-t py-2">
				<Spinner />
			</div>
		{/if}
	{/if}
</div>