<script lang="ts">
	import { withBase } from '$lib/paths';
	import type { SpanDto } from '$lib/traces-api';
	import { Badge } from '$lib/components/ui/badge';
	import { statusVariant, statusLabel, rolledUpStatusCode } from '$lib/traces/status';
	import { formatDurationNano } from '$lib/traces/duration';
	import { formatRowTimestamp } from '$lib/time/format';

	let { trace }: { trace: SpanDto } = $props();

	// Rolled up across every span in the trace, not just this root row's own statusCode -
	// see rolledUpStatusCode's remarks.
	let displayStatusCode = $derived(rolledUpStatusCode(trace));
</script>

<!-- A real link, not a button + goto(), so Ctrl/Cmd/middle-click and "Open in new tab" work. -->
<a
	href={withBase(`/traces/${trace.traceId}`)}
	class="hover:bg-muted/50 focus-visible:bg-muted/50 grid w-full items-center gap-3 border-b px-3 text-left text-sm focus-visible:outline-none"
	style="grid-template-columns: var(--trace-row-columns); height: var(--trace-row-height);"
>
	<span class="text-muted-foreground truncate font-mono text-xs">{formatRowTimestamp(trace.startTime)}</span>
	<span><Badge variant={statusVariant(displayStatusCode)}>{statusLabel(displayStatusCode)}</Badge></span>
	<span class="truncate">{trace.serviceName || '—'}</span>
	<span class="truncate">{trace.name || '—'}</span>
	<span class="text-muted-foreground truncate font-mono text-xs">{formatDurationNano(trace.durationNano)}</span>
	<!-- A 200ms trace with 2 spans and one with 80 read very differently - see
	     SpanDto.spanCount's remarks. Only absent for pre-rollout cached data, hence the
	     "—" fallback rather than assuming it's always present. -->
	<span class="text-muted-foreground truncate text-right font-mono text-xs">{trace.spanCount ?? '—'}</span>
</a>
