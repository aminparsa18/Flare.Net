<script lang="ts">
	// Each *FlushWorker*'s own last-flush outcome (Planning.md v10) - a stuck/erroring
	// worker is exactly the "why did my logs stop showing up" case this whole item exists
	// to diagnose, and it's invisible from the stream table alone (a growing stream/PEL is
	// a symptom; this is the cause).
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatAge, formatCount, secondsSince } from '$lib/ingestion/format';

	const ingestion = ingestionContext.get();

	const workers = $derived(ingestion.pipeline?.flushWorkers ?? []);
</script>

<div class="px-4 pb-4">
	<h2 class="mb-2 text-sm font-medium">Flush workers</h2>
	<Table.Root>
		<Table.Header>
			<Table.Row>
				<Table.Head>Signal</Table.Head>
				<Table.Head class="text-right">Last flush</Table.Head>
				<Table.Head class="text-right">Batch size</Table.Head>
				<Table.Head class="text-right">Consecutive errors</Table.Head>
				<Table.Head>Last error</Table.Head>
			</Table.Row>
		</Table.Header>
		<Table.Body>
			{#each workers as worker (worker.signal)}
				<Table.Row>
					<Table.Cell class="font-medium">{worker.signal}</Table.Cell>
					<Table.Cell class="text-muted-foreground text-right tabular-nums">
						{worker.lastFlushAt ? formatAge(secondsSince(worker.lastFlushAt)) : 'never'}
					</Table.Cell>
					<Table.Cell class="text-right tabular-nums">
						{worker.lastBatchSize === null ? '—' : formatCount(worker.lastBatchSize)}
					</Table.Cell>
					<Table.Cell class="text-right tabular-nums">
						{#if worker.consecutiveErrors > 0}
							<Badge variant="destructive">{formatCount(worker.consecutiveErrors)}</Badge>
						{:else}
							0
						{/if}
					</Table.Cell>
					<Table.Cell class="text-destructive max-w-xs truncate font-mono text-xs" title={worker.lastError ?? undefined}>
						{worker.lastError ?? '—'}
					</Table.Cell>
				</Table.Row>
			{/each}
		</Table.Body>
	</Table.Root>
</div>
