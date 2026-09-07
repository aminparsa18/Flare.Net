<script lang="ts">
	// The part of this page that's honest about what "indexing" means for a ClickHouse-
	// backed product: nothing here is user-created (see IndexingQueryService's remarks) -
	// this table exists so the schema work in db/clickhouse/*.sql is visible, not so a
	// user can add/remove a row here the way Seq's own λ/signal indexes work.
	//
	// No section heading of its own on purpose - it renders directly under
	// IndexingQueryOptimization's "Query optimization" heading and "N indexes across M
	// tables" summary line, which already give this table its context ("here's the detail
	// behind that count") instead of presenting as a standalone "here's every index"
	// inventory.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { indexingContext } from '$lib/indexing/context';
	import { formatBytes } from '$lib/indexing/format';
	import * as m from '$lib/paraglide/messages';

	const indexing = indexingContext.get();
</script>

<div class="px-4 pb-4">
	{#if !indexing.stats || indexing.stats.skipIndexes.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Title>{m.indexingSkipIndexesTable_noSkipIndexes()}</Empty.Title>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.indexingSkipIndexesTable_tableColumn()}</Table.Head>
					<Table.Head>{m.indexingSkipIndexesTable_indexColumn()}</Table.Head>
					<Table.Head>{m.indexingSkipIndexesTable_typeColumn()}</Table.Head>
					<Table.Head>{m.indexingSkipIndexesTable_expressionColumn()}</Table.Head>
					<Table.Head class="text-right">{m.indexingSkipIndexesTable_compressedColumn()}</Table.Head>
					<Table.Head class="text-right">{m.indexingSkipIndexesTable_uncompressedColumn()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each indexing.stats.skipIndexes as index (index.tableName + index.indexName)}
					<Table.Row>
						<Table.Cell class="font-medium">{index.tableName}</Table.Cell>
						<Table.Cell class="font-mono text-xs">{index.indexName}</Table.Cell>
						<Table.Cell><Badge variant="outline">{index.type}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground max-w-64 truncate font-mono text-xs" title={index.expression}>
							{index.expression}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatBytes(index.compressedBytes)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatBytes(index.uncompressedBytes)}</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
