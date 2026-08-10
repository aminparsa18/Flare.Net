<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { indexingContext } from '$lib/indexing/context';
	import { formatBytes, formatCount, formatRatio } from '$lib/indexing/format';

	const indexing = indexingContext.get();
</script>

<div class="px-4 pb-4">
	<h2 class="mb-2 text-sm font-medium">Tables</h2>
	{#if indexing.loading && !indexing.stats}
		<div class="flex h-32 items-center justify-center">
			<Spinner />
		</div>
	{:else if !indexing.stats || indexing.stats.tables.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Title>No tables found</Empty.Title>
				<Empty.Description>Flare.Api's ClickHouse migrations haven't run against this database yet.</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>Table</Table.Head>
					<Table.Head>Engine</Table.Head>
					<Table.Head>Sorting key</Table.Head>
					<Table.Head class="text-right">Rows</Table.Head>
					<Table.Head class="text-right">Parts</Table.Head>
					<Table.Head class="text-right">Compressed</Table.Head>
					<Table.Head class="text-right">Uncompressed</Table.Head>
					<Table.Head class="text-right">Ratio</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each indexing.stats.tables as table (table.tableName)}
					<Table.Row>
						<Table.Cell class="font-medium">{table.tableName}</Table.Cell>
						<Table.Cell><Badge variant="outline">{table.engine}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground max-w-64 truncate font-mono text-xs" title={table.sortingKey}>
							{table.sortingKey}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(table.rows)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(table.activeParts)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatBytes(table.compressedBytes)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatBytes(table.uncompressedBytes)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatRatio(table.compressedBytes, table.uncompressedBytes)}</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
