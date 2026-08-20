<script lang="ts">
	// Redis Stream / consumer-group depth per signal (Planning.md v10) - "is the buffered
	// pipeline keeping up", not "is data arriving" (IngestionSignalsTable, v8). Always a
	// live snapshot (no window - see PipelineQueryService's remarks), so this doesn't
	// react to the page's window-preset selector the way the tiles/chart/signals table do.
	//
	// Renamed from "Stream buffers" (feedback: "Stream" reads as the user-facing OTel signal
	// concept, not the Redis-Streams buffering mechanism this table is actually about) and
	// given a bar + %-of-capacity per signal instead of a bare count (feedback: "1M by itself
	// doesn't tell me whether that's healthy" - matches OpenTelemetry's own guidance to watch
	// queue size *against capacity*, ~60-70% being the point to start considering scaling).
	// Thresholds/computation live in ingestion/health.ts, shared with the page-level verdict
	// so the two never disagree about what "getting full" means.
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatAge, formatCount } from '$lib/ingestion/format';
	import { DOWN_UTILIZATION_PERCENT, WARN_UTILIZATION_PERCENT, isBacklogStuck, utilizationPercent } from '$lib/ingestion/health';

	const ingestion = ingestionContext.get();

	const streams = $derived(ingestion.pipeline?.streams ?? []);

	function barClass(pct: number | null): string {
		if (pct === null) return 'bg-muted-foreground/50';
		if (pct >= DOWN_UTILIZATION_PERCENT) return 'bg-destructive';
		if (pct >= WARN_UTILIZATION_PERCENT) return 'bg-warning';
		return 'bg-primary';
	}

	function pctClass(pct: number | null): string {
		if (pct === null) return 'text-muted-foreground';
		if (pct >= DOWN_UTILIZATION_PERCENT) return 'text-destructive';
		if (pct >= WARN_UTILIZATION_PERCENT) return 'text-warning';
		return 'text-muted-foreground';
	}
</script>

<div class="px-4 pb-4">
	<h2 class="mb-2 text-sm font-medium">Pipeline buffers</h2>
	<Table.Root>
		<Table.Header>
			<Table.Row>
				<Table.Head>Signal</Table.Head>
				<Table.Head class="text-right">Buffer utilization</Table.Head>
				<Table.Head class="text-right">Lag</Table.Head>
				<Table.Head
					class="text-right"
					title="Delivered to the flush worker, not yet acknowledged - normal in small numbers while a batch is in flight. Only colored once an entry has actually sat unacked for 5+ minutes, not just for being nonzero."
				>
					Pending
				</Table.Head>
				<Table.Head class="text-right">Consumers</Table.Head>
				<Table.Head class="text-right">Oldest pending</Table.Head>
			</Table.Row>
		</Table.Header>
		<Table.Body>
			{#each streams as stream (stream.signal)}
				{@const pct = utilizationPercent(stream)}
				<Table.Row>
					<Table.Cell class="flex items-center gap-2">
						<span class="font-medium">{stream.signal}</span>
						{#if !stream.available}
							<Badge variant="outline">no traffic yet</Badge>
						{/if}
					</Table.Cell>
					<Table.Cell class="text-right">
						<div class="ml-auto flex w-40 flex-col items-end gap-1">
							<span class="tabular-nums">
								{formatCount(stream.length)}{#if stream.capacity > 0}<span class="text-muted-foreground"> / {formatCount(stream.capacity)}</span>{/if}
							</span>
							{#if pct !== null}
								<div class="flex w-full items-center gap-1.5">
									<div class="bg-muted h-1.5 min-w-0 flex-1 overflow-hidden rounded-full">
										<div class="h-full rounded-full {barClass(pct)}" style="width: {Math.min(100, pct)}%"></div>
									</div>
									<span class="w-8 shrink-0 text-right text-xs tabular-nums {pctClass(pct)}">{pct}%</span>
								</div>
							{/if}
						</div>
					</Table.Cell>
					<Table.Cell class="text-right tabular-nums {stream.lag && stream.lag > 0 ? 'text-warning' : ''}">
						{stream.lag === null ? '—' : formatCount(stream.lag)}
					</Table.Cell>
					<Table.Cell class="text-right tabular-nums {isBacklogStuck(stream) ? 'text-warning' : ''}">
						{formatCount(stream.pendingCount)}
					</Table.Cell>
					<Table.Cell class="text-right tabular-nums">{stream.consumers}</Table.Cell>
					<Table.Cell class="text-muted-foreground text-right tabular-nums">
						{formatAge(stream.oldestPendingAgeSeconds)}
					</Table.Cell>
				</Table.Row>
			{/each}
		</Table.Body>
	</Table.Root>
</div>