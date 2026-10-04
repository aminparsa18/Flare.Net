<script lang="ts">
	import { explorerPrefs } from '$lib/explorer/prefs.svelte';
	import { regional } from '$lib/regional/prefs.svelte';
	import { withBase } from '$lib/paths';
	import VirtualList from '$lib/components/virtual-list/VirtualList.svelte';
	import LogRow from './LogRow.svelte';
	import * as Empty from '$lib/components/ui/empty';
	import CaseSuggestions from '$lib/components/CaseSuggestions.svelte';
	import { Lottie } from '$lib/components/ui/lottie';
	import { Spinner } from '$lib/components/ui/spinner';
	import { logsExplorerContext } from '$lib/logs/context';
	import type { LogEventDto } from '$lib/api';
	import { logRowHeight } from '$lib/logs/state.svelte';
	import * as m from '$lib/paraglide/messages';

	const explorer = logsExplorerContext.get();

	let list = $state<ReturnType<typeof VirtualList<LogEventDto>> | null>(null);
	// Keeps the row behind the details drawer in view as prev/next moves the selection.
	$effect(() => {
		const i = explorer.selectedIndex;
		if (i >= 0) list?.scrollToIndex(i);
	});

	// Uniform per mode, not per row - VirtualList only supports one fixed row height, so
	// "N lines" means every row is N lines tall and LogRow line-clamps the body to fit.
	const lines = $derived(explorer.filter.maxLinesPerRow);
	const ROW_HEIGHT = $derived(logRowHeight(lines));
	// Shared between the header and every LogRow via CSS custom properties (set once
	// here, per svelte-best-practices' style:--prop guidance) so the two can never drift
	// out of alignment the way two hand-copied grid-template-columns strings could.
	// Duration column drops out entirely while live: live-tailed rows come straight off
	// the Redis Stream, before ever reaching ClickHouse (see EventDetailSheet's/
	// LogRow's own remarks), so their spanDurationNano is always absent - a column of
	// nothing but "—" for every visible row is worse than no column at all. $derived
	// (not const) so toggling live re-flows both the header and every row together.
	// Time and Message can also be hidden (LogsFilterState.showTimestampColumn/
	// showBodyColumn); whichever column ends up last takes the remaining width (1fr).
	const showTime = $derived(explorer.filter.showTimestampColumn);
	const showBody = $derived(explorer.filter.showBodyColumn);
	const bodyColumns = $derived(explorer.filter.bodyColumns);
	let COLUMNS = $derived.by(() => {
		const cols = [
			showTime && (regional.timeFormat === '12h' ? '210px' : '170px'), // fits the Time column's fixed "MM-DD HH:mm:ss.SSS" width (+ " PM" in 12h)
			'90px',
			'160px',
			!explorer.live && '90px', // fits Duration's widest realistic value (e.g. "12.34s")
			...bodyColumns.map(() => '140px'),
			showBody && '1fr'
		].filter((c): c is string => !!c);
		if (!showBody) cols[cols.length - 1] = `minmax(${cols[cols.length - 1]}, 1fr)`;
		return cols.join(' ');
	});
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
		{#if showTime}
			<span>{m.logsTable_colTime()}</span>
		{/if}
		<span>{m.logsTable_colLevel()}</span>
		<span>{m.logsTable_colService()}</span>
		{#if !explorer.live}
			<span>{m.logsTable_colDuration()}</span>
		{/if}
		{#each bodyColumns as path (path)}
			<span class="truncate font-mono" title={path}>{path}</span>
		{/each}
		{#if showBody}
			<span>{m.logsTable_colMessage()}</span>
		{/if}
	</div>

	{#if explorer.events.length === 0 && !explorer.loading}
		<Empty.Root class="flex-1">
			<Empty.Media class="size-34">
				<!-- Always autoplay - the animation draws its icon in from an empty first frame,
				     so a non-live "no match" state would render blank without at least one
				     play-through. Only *loop* while live: a filtered/no-match search isn't
				     "waiting for something to happen", so it plays once and rests on the last frame. -->
				<Lottie src={withBase('/no_log.json')} loop={explorer.live} autoplay class="size-full" />
			</Empty.Media>
			<Empty.Header>
				<Empty.Title>{explorer.live ? m.logsTable_waitingTitle() : m.logsTable_noEventsTitle()}</Empty.Title>
				<Empty.Description>
					{explorer.live ? m.logsTable_waitingDescription() : m.logsTable_noEventsDescription()}
				</Empty.Description>
			</Empty.Header>
			{#if !explorer.live}
				<Empty.Content>
					<CaseSuggestions suggestions={explorer.caseSuggestions} onApply={(s) => explorer.applyCaseSuggestion(s)} />
				</Empty.Content>
			{/if}
			{#if explorer.live}
				<!-- Only for the live/nothing-has-arrived-yet case, not the filtered/no-match one -
				     a search that just doesn't match anything isn't a "how do I send logs" moment. -->
				<Empty.Content>
					<a href={withBase('/data-sources')} class="text-muted-foreground hover:text-foreground text-xs underline underline-offset-4">
						{m.logsTable_seeHowToIngest()}
					</a>
				</Empty.Content>
			{/if}
		</Empty.Root>
	{:else}
		<VirtualList
			bind:this={list}
			items={explorer.events}
			itemHeight={ROW_HEIGHT}
			getKey={(event) => event.eventId}
			ariaLabel={m.logsTable_ariaLabel()}
			onEndReached={() => void explorer.loadMore()}
			followNewest={!explorer.live || explorerPrefs.liveAutoScroll}
			class="min-h-0 flex-1"
		>
			{#snippet children(event)}
				<LogRow {event} {lines} {showTime} {showBody} {bodyColumns} live={explorer.live} onSelect={(e) => (explorer.selectedEventId = e.eventId)} />
			{/snippet}
		</VirtualList>
		{#if explorer.loadingMore}
			<div class="flex shrink-0 items-center justify-center border-t py-2">
				<Spinner />
			</div>
		{/if}
	{/if}
</div>