<script lang="ts">
	import { Badge } from '#lib/components/ui/badge/index.ts';
	import * as m from '#lib/paraglide/messages.js';

	/**
	 * OTLP dropped-item counts (ADR-0272): how many attributes/events/links the app's SDK discarded at its
	 * `OTEL_*_COUNT_LIMIT` before sending. Renders nothing when all are 0 (the usual case, and every row stored before
	 * migration 0091), so callers can place it unconditionally.
	 */
	let { attributes = 0, events = 0, links = 0 }: { attributes?: number; events?: number; links?: number } = $props();
</script>

{#if attributes > 0 || events > 0 || links > 0}
	<div class="flex flex-wrap gap-2" title={m.droppedCounts_hint()}>
		{#if attributes > 0}
			<Badge variant="warning">{m.droppedCounts_attributes({ count: attributes })}</Badge>
		{/if}
		{#if events > 0}
			<Badge variant="warning">{m.droppedCounts_events({ count: events })}</Badge>
		{/if}
		{#if links > 0}
			<Badge variant="warning">{m.droppedCounts_links({ count: links })}</Badge>
		{/if}
	</div>
{/if}
