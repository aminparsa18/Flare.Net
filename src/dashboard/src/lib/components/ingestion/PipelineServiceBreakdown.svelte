<script lang="ts">
	// Per-service.name arrivals/bytes breakdown (Planning.md v10) - folded in from the
	// original Ingestion page proposal (Planning.md's own "Later" note). One compact table
	// per signal that actually has traffic, capped to the top N services
	// (PipelineQueryService.TopServicesPerSignal) with an "+N more" row for the rest - same
	// convention MetricChart/IndexingGrowthChart already use for a bounded legend.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatBytes, formatCount } from '$lib/ingestion/format';

	const ingestion = ingestionContext.get();

	const breakdowns = $derived(
		(ingestion.pipeline?.serviceBreakdowns ?? []).filter((b) => b.topServices.length > 0 || b.otherServiceCount > 0)
	);
</script>

<div class="px-4 pb-4">
	<h2 class="mb-2 text-sm font-medium">Services by signal</h2>
	{#if breakdowns.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Title>No service.name data yet</Empty.Title>
				<Empty.Description>Populated once an OTLP export carries a resource with a service.name attribute.</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<div class="grid gap-4 lg:grid-cols-3">
			{#each breakdowns as breakdown (breakdown.signal)}
				<div>
					<h3 class="text-muted-foreground mb-1 text-xs font-medium">{breakdown.signal}</h3>
					<Table.Root>
						<Table.Header>
							<Table.Row>
								<Table.Head>Service</Table.Head>
								<Table.Head class="text-right">Events</Table.Head>
								<Table.Head class="text-right">Bytes</Table.Head>
							</Table.Row>
						</Table.Header>
						<Table.Body>
							{#each breakdown.topServices as entry (entry.serviceName)}
								<Table.Row>
									<Table.Cell class="max-w-32 truncate font-mono text-xs" title={entry.serviceName}>{entry.serviceName}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{formatCount(entry.records)}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{formatBytes(entry.bytes)}</Table.Cell>
								</Table.Row>
							{/each}
							{#if breakdown.otherServiceCount > 0}
								<Table.Row>
									<Table.Cell class="text-muted-foreground text-xs">+{breakdown.otherServiceCount} more</Table.Cell>
									<Table.Cell class="text-muted-foreground text-right tabular-nums">{formatCount(breakdown.otherRecords)}</Table.Cell>
									<Table.Cell class="text-muted-foreground text-right tabular-nums">{formatBytes(breakdown.otherBytes)}</Table.Cell>
								</Table.Row>
							{/if}
						</Table.Body>
					</Table.Root>
				</div>
			{/each}
		</div>
	{/if}
</div>
