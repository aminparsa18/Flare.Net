<script lang="ts">
	import type { LogEventDto } from '$lib/api';
	import { Badge } from '$lib/components/ui/badge';
	import { severityVariant } from '$lib/logs/severity';
	import { formatDurationNano } from '$lib/traces/duration';

	let {
		event,
		live,
		lines = 1,
		onSelect
	}: {
		event: LogEventDto;
		live: boolean;
		/** Message-column line count (LogsFilterState.maxLinesPerRow) - the row's own height comes from LogTable's --log-row-height, sized to match via logRowHeight. */
		lines?: number;
		onSelect: (event: LogEventDto) => void;
	} = $props();

	const multiline = $derived(lines > 1);

	// Hand-formatted rather than toLocaleString/toLocaleDateString - this is a monospace
	// technical column (font-mono below), and locale date formats vary in width (e.g. "Aug 9"
	// vs "Aug 12"), which would make the column jitter row to row. Fixed-width zero-padded
	// MM-DD keeps every row exactly the same character count.
	function formatTime(iso: string): string {
		const d = new Date(iso);
		const pad = (n: number, len = 2) => String(n).padStart(len, '0');
		const date = `${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
		const time = `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`;
		return `${date} ${time}`;
	}
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
	<span class="text-muted-foreground truncate font-mono text-xs leading-5">{formatTime(event.timestamp)}</span>
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
	{#if multiline}
		<!-- pre-wrap keeps the body's own newlines (stack traces, pretty-printed JSON) rather
		     than collapsing them; line-clamp cuts it at exactly `lines` lines so it can never
		     outgrow the fixed row height VirtualList positions rows by. -->
		<span
			class="overflow-hidden leading-5 break-words whitespace-pre-wrap"
			style="display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: {lines}; line-clamp: {lines};"
		>{event.body}</span>
	{:else}
		<span class="truncate leading-5">{event.body}</span>
	{/if}
</button>
