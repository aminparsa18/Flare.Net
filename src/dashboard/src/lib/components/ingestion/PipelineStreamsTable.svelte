<script lang="ts">
	// Redis Stream / consumer-group depth per signal (Planning.md v10) - "is the buffered
	// pipeline keeping up", not "is data arriving" (IngestionSignalsTable, v8). Always a
	// live snapshot (no window - see PipelineQueryService's remarks), so this doesn't
	// react to the page's window-preset selector the way the tiles/chart/signals table do.
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatAge, formatCount } from '$lib/ingestion/format';

	const ingestion = ingestionContext.get();

	const streams = $derived(ingestion.pipeline?.streams ?? []);
</script>

<div class="px-4 pb-4">
	<h2 class="mb-2 text-sm font-medium">Stream buffers</h2>
	<Table.Root>
		<Table.Header>
			<Table.Row>
				<Table.Head>Signal</Table.Head>
				<Table.Head class="text-right">Buffered</Table.Head>
				<Table.Head class="text-right">Lag</Table.Head>
				<Table.Head class="text-right">Pending</Table.Head>
				<Table.Head class="text-right">Consumers</Table.Head>
				<Table.Head class="text-right">Oldest pending</Table.Head>
			</Table.Row>
		</Table.Header>
		<Table.Body>
			{#each streams as stream (stream.signal)}
				<Table.Row>
					<Table.Cell class="flex items-center gap-2">
						<span class="font-medium">{stream.signal}</span>
						{#if !stream.available}
							<Badge variant="outline">no traffic yet</Badge>
						{/if}
					</Table.Cell>
					<Table.Cell class="text-right tabular-nums">{formatCount(stream.length)}</Table.Cell>
					<Table.Cell class="text-right tabular-nums {stream.lag && stream.lag > 0 ? 'text-warning' : ''}">
						{stream.lag === null ? '—' : formatCount(stream.lag)}
					</Table.Cell>
					<Table.Cell class="text-right tabular-nums {stream.pendingCount > 0 ? 'text-warning' : ''}">
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
