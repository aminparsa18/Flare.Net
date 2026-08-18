<script lang="ts">
	import { goto } from '$app/navigation';
	import type { SpanDto } from '$lib/traces-api';
	import { Badge } from '$lib/components/ui/badge';
	import { statusVariant, statusLabel } from '$lib/traces/status';
	import { formatDurationNano } from '$lib/traces/duration';

	let { trace }: { trace: SpanDto } = $props();

	// Same hand-formatted, fixed-width time convention as LogRow.formatTime - a
	// monospace technical column shouldn't jitter row to row with locale-varying widths.
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
	class="hover:bg-muted/50 focus-visible:bg-muted/50 grid w-full items-center gap-3 border-b px-3 text-left text-sm focus-visible:outline-none"
	style="grid-template-columns: var(--trace-row-columns); height: var(--trace-row-height);"
	onclick={() => goto(`/traces/${trace.traceId}`)}
>
	<span class="text-muted-foreground truncate font-mono text-xs">{formatTime(trace.startTime)}</span>
	<span><Badge variant={statusVariant(trace.statusCode)}>{statusLabel(trace.statusCode)}</Badge></span>
	<span class="truncate">{trace.serviceName || '—'}</span>
	<span class="truncate">{trace.name || '—'}</span>
	<span class="text-muted-foreground truncate font-mono text-xs">{formatDurationNano(trace.durationNano)}</span>
	<!-- A 200ms trace with 2 spans and one with 80 read very differently - see
	     SpanDto.spanCount's remarks. Only absent for pre-rollout cached data, hence the
	     "—" fallback rather than assuming it's always present. -->
	<span class="text-muted-foreground truncate text-right font-mono text-xs">{trace.spanCount ?? '—'}</span>
</button>
