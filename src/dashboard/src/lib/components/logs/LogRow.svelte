<script lang="ts">
	import type { LogEventDto } from '$lib/api';
	import { Badge } from '$lib/components/ui/badge';
	import { severityVariant } from '$lib/logs/severity';
	import { formatDurationNano } from '$lib/traces/duration';

	let { event, onSelect }: { event: LogEventDto; onSelect: (event: LogEventDto) => void } = $props();

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
	class="hover:bg-muted/50 focus-visible:bg-muted/50 grid w-full items-center gap-3 overflow-hidden border-b px-3 text-left text-sm focus-visible:outline-none"
	style="grid-template-columns: var(--log-row-columns); height: var(--log-row-height); min-height: var(--log-row-height); max-height: var(--log-row-height);"
	onclick={() => onSelect(event)}
>
	<span class="text-muted-foreground truncate font-mono text-xs">{formatTime(event.timestamp)}</span>
	<span><Badge variant={severityVariant(event.severityNumber)}>{event.severityText || '—'}</Badge></span>
	<span class="truncate">{event.serviceName || '—'}</span>
	<span class="text-muted-foreground truncate font-mono text-xs">
		{event.spanDurationNano !== undefined ? formatDurationNano(event.spanDurationNano) : '—'}
	</span>
	<span class="truncate">{event.body}</span>
</button>
