<script lang="ts">
	import VirtualList from '$lib/components/virtual-list/VirtualList.svelte';
	import LogRow from './LogRow.svelte';
	import * as Empty from '$lib/components/ui/empty';
	import { Lottie } from '$lib/components/ui/lottie';
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
	<!-- overflow-y: hidden + scrollbar-gutter: stable reserves the same width VirtualList's
	     actual scrollbar eats into below - without it, this header (never itself scrollable)
	     would be a few px wider than the rows once there's enough data to scroll, throwing the
	     rightmost column (Message) out of alignment even with gap-3 matching. -->
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
			<Empty.Media class="size-34">
				<!-- Always autoplay - the animation draws its icon in from an empty first frame,
				     so a non-live "no match" state would render blank without at least one
				     play-through. Only *loop* while live: a filtered/no-match search isn't
				     "waiting for something to happen", so it plays once and rests on the last frame. -->
				<Lottie src="/no_log.json" loop={explorer.live} autoplay class="size-full" />
			</Empty.Media>
			<Empty.Header>
				<Empty.Title>{explorer.live ? 'Waiting for events' : 'No events'}</Empty.Title>
				<Empty.Description>
					{explorer.live
						? 'Live tail is connected — new events will appear here the moment they arrive.'
						: 'No events match the current filters. Try widening the time range or clearing a filter.'}
				</Empty.Description>
			</Empty.Header>
			{#if explorer.live}
				<!-- Only for the live/nothing-has-arrived-yet case, not the filtered/no-match one -
				     a search that just doesn't match anything isn't a "how do I send logs" moment. -->
				<Empty.Content>
					<a href="/data-sources" class="text-muted-foreground hover:text-foreground text-xs underline underline-offset-4">
						See how to ingest data →
					</a>
				</Empty.Content>
			{/if}
		</Empty.Root>
	{:else}
		<VirtualList
			items={explorer.events}
			itemHeight={ROW_HEIGHT}
			getKey={(event) => event.eventId}
			ariaLabel="Log events"
			onEndReached={() => void explorer.loadMore()}
			class="min-h-0 flex-1"
		>
			{#snippet children(event)}
				<LogRow {event} onSelect={(e) => (explorer.selectedEventId = e.eventId)} />
			{/snippet}
		</VirtualList>
		{#if explorer.loadingMore}
			<div class="flex shrink-0 items-center justify-center border-t py-2">
				<Spinner />
			</div>
		{/if}
	{/if}
</div>