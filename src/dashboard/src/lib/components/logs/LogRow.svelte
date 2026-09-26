<script lang="ts">
	import type { LogEventDto } from '$lib/api';
	import { Badge } from '$lib/components/ui/badge';
	import AnsiText from './AnsiText.svelte';
	import { severityVariant } from '$lib/logs/severity';
	import { formatDurationNano } from '$lib/traces/duration';
	// Fixed-width MM-DD HH:mm:ss.SSS - this is a monospace column (font-mono below) that
	// mustn't jitter row to row; see $lib/time/format.
	import { formatRowTimestamp } from '$lib/time/format';

	let {
		event,
		live,
		lines = 1,
		showTime = true,
		showBody = true,
		onSelect
	}: {
		event: LogEventDto;
		live: boolean;
		/** Message-column line count (LogsFilterState.maxLinesPerRow) - the row's own height comes from LogTable's --log-row-height, sized to match via logRowHeight. */
		lines?: number;
		/** LogsFilterState.showTimestampColumn/showBodyColumn - must match LogTable's header, which also drops the column from --log-row-columns. */
		showTime?: boolean;
		showBody?: boolean;
		onSelect: (event: LogEventDto) => void;
	} = $props();

	const multiline = $derived(lines > 1);

</script>

<button
	type="button"
	class={[
		'hover:bg-muted/50 focus-visible:bg-muted/50 grid w-full gap-3 overflow-hidden border-b px-3 text-left text-sm focus-visible:outline-none',
		// Multi-line rows pin every column to the body's first line (py-1.5 + a 20px
		// leading-5 line box each) instead of centering Time/Level/Service against a tall
		// wrapped message. 6px + N x 20px + 6px is exactly logRowHeight(N).
		multiline ? 'items-start py-1.5' : 'items-center'
	]}
	style="grid-template-columns: var(--log-row-columns); height: var(--log-row-height); min-height: var(--log-row-height); max-height: var(--log-row-height);"
	onclick={() => onSelect(event)}
>
	{#if showTime}
		<span class="text-muted-foreground truncate font-mono text-xs leading-5">{formatRowTimestamp(event.timestamp)}</span>
	{/if}
	<span class="flex h-5 items-center"><Badge variant={severityVariant(event.severityNumber)}>{event.severityText || '—'}</Badge></span>
	<span class="truncate leading-5">{event.serviceName || '—'}</span>
	{#if !live}
		<!-- != null (not !== undefined) - System.Text.Json serializes the unset LogEventDto.SpanDurationNano
		     as JSON null, not an omitted key, so a stricter undefined-only check let a null
		     duration slip through to formatDurationNano(null) and print "0" instead of "—". -->
		<span class="text-muted-foreground truncate font-mono text-xs leading-5">
			{event.spanDurationNano != null ? formatDurationNano(event.spanDurationNano) : '—'}
		</span>
	{/if}
	{#if showBody && multiline}
		<!-- pre-wrap keeps the body's own newlines (stack traces, pretty-printed JSON) rather
		     than collapsing them; line-clamp cuts it at exactly `lines` lines so it can never
		     outgrow the fixed row height VirtualList positions rows by. -->
		<span
			class="overflow-hidden leading-5 break-words whitespace-pre-wrap"
			style="display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: {lines}; line-clamp: {lines};"
		><AnsiText text={event.body} /></span>
	{:else if showBody}
		<span class="truncate leading-5"><AnsiText text={event.body} /></span>
	{/if}
</button>
