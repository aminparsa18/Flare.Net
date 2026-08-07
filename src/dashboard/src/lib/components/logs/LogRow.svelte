<script lang="ts">
	import type { LogEventDto } from '$lib/api';
	import { Badge } from '$lib/components/ui/badge';
	import { severityVariant } from '$lib/logs/severity';

	let { event, onSelect }: { event: LogEventDto; onSelect: (event: LogEventDto) => void } = $props();

	function formatTime(iso: string): string {
		const d = new Date(iso);
		return d.toLocaleTimeString(undefined, { hour12: false }) + '.' + String(d.getMilliseconds()).padStart(3, '0');
	}
</script>

<button
	type="button"
	class="hover:bg-muted/50 focus-visible:bg-muted/50 grid w-full items-center gap-3 border-b px-3 text-left text-sm focus-visible:outline-none"
	style="grid-template-columns: var(--log-row-columns); height: var(--log-row-height);"
	onclick={() => onSelect(event)}
>
	<span class="text-muted-foreground truncate font-mono text-xs">{formatTime(event.timestamp)}</span>
	<span><Badge variant={severityVariant(event.severityNumber)}>{event.severityText || '—'}</Badge></span>
	<span class="truncate">{event.serviceName || '—'}</span>
	<span class="truncate">{event.body}</span>
</button>
