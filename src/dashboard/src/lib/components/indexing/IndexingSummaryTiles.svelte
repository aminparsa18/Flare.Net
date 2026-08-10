<script lang="ts">
	import * as Card from '$lib/components/ui/card';
	import { indexingContext } from '$lib/indexing/context';
	import { formatBytes, formatCount } from '$lib/indexing/format';

	const indexing = indexingContext.get();

	const totals = $derived.by(() => {
		const tables = indexing.stats?.tables ?? [];
		return {
			tableCount: tables.filter((t) => t.rows > 0).length,
			totalRows: tables.reduce((sum, t) => sum + t.rows, 0),
			totalCompressed: tables.reduce((sum, t) => sum + t.compressedBytes, 0),
			totalUncompressed: tables.reduce((sum, t) => sum + t.uncompressedBytes, 0),
			indexCount: indexing.stats?.skipIndexes.length ?? 0
		};
	});
</script>

<div class="grid grid-cols-2 gap-3 p-4 lg:grid-cols-4">
	<Card.Root>
		<Card.Header>
			<Card.Description>Total storage</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatBytes(totals.totalCompressed)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">
			{formatBytes(totals.totalUncompressed)} uncompressed
		</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>Total rows</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatCount(totals.totalRows)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">across {totals.tableCount} tables with data</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>Tables</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{indexing.stats?.tables.length ?? 0}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">in this ClickHouse database</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>Skip indexes</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{totals.indexCount}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">schema-defined, not user-managed</Card.Content>
	</Card.Root>
</div>
