<script lang="ts">
	// Flare's analog to Seq's "Ingestion Log" link - unlike Seq (a dedicated screen), this
	// is inline: the recent-errors list is already small (capped at 200 server-side, see
	// IngestionStatsKeys.MaxErrorEntries) and this is the only page that would ever want
	// it, so a separate route would just be an extra click for no benefit.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { ingestionContext } from '$lib/ingestion/context';

	const ingestion = ingestionContext.get();

	const errors = $derived(ingestion.stats?.recentErrors ?? []);

	function formatTime(iso: string): string {
		return new Date(iso).toLocaleString(undefined, { hour12: false });
	}
</script>

<div class="flex min-h-0 flex-1 flex-col border-t px-4 py-3">
	<h2 class="mb-2 text-sm font-medium">Ingestion log</h2>
	{#if errors.length === 0}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Title>No rejected payloads</Empty.Title>
				<Empty.Description>Malformed OTLP exports (bad protobuf/JSON, unsupported content type) will show up here.</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div class="min-h-0 flex-1 overflow-auto">
			<Table.Root>
				<Table.Header>
					<Table.Row>
						<Table.Head>Time</Table.Head>
						<Table.Head>Receiver</Table.Head>
						<Table.Head>Reason</Table.Head>
					</Table.Row>
				</Table.Header>
				<Table.Body>
					{#each errors as entry, i (entry.timestamp + i)}
						<Table.Row>
							<Table.Cell class="text-muted-foreground whitespace-nowrap text-xs">{formatTime(entry.timestamp)}</Table.Cell>
							<Table.Cell>
								<Badge variant="outline">{entry.signal} · {entry.protocol}</Badge>
							</Table.Cell>
							<Table.Cell class="text-destructive font-mono text-xs">{entry.reason}</Table.Cell>
						</Table.Row>
					{/each}
				</Table.Body>
			</Table.Root>
		</div>
	{/if}
</div>
