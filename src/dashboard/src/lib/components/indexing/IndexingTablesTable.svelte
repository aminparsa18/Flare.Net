<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import * as Popover from '$lib/components/ui/popover';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import InfoIcon from '@lucide/svelte/icons/info';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import { indexingContext } from '$lib/indexing/context';
	import { formatBytes, formatCount, formatRatio, formatTableGrowth } from '$lib/indexing/format';
	import { computePartsHealth, HIGH_FRAGMENTATION_PARTS } from '$lib/indexing/health';

	const indexing = indexingContext.get();

	// Per-table sum of the same 30-day growth series the chart above plots - reused rather
	// than re-fetched, since "bytes this table added recently" is already exactly what that
	// series carries.
	const addedBytesByTable = $derived.by(() => {
		const sums = new Map<string, number>();
		for (const point of indexing.stats?.growth ?? []) {
			sums.set(point.tableName, (sums.get(point.tableName) ?? 0) + point.bytes);
		}
		return sums;
	});

	function growthClass(addedBytes: number, compressedBytes: number): string {
		if (compressedBytes <= 0 || addedBytes <= 0) return '';
		const percent = (addedBytes / compressedBytes) * 100;
		if (percent >= 100) return 'text-destructive';
		if (percent >= 50) return 'text-warning';
		return '';
	}
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
					<Table.Head class="text-right">
						<span class="inline-flex items-center justify-end gap-1">
							Parts
							<Popover.Root>
								<Popover.Trigger>
									{#snippet child({ props })}
										<button
											{...props}
											type="button"
											class="text-muted-foreground hover:text-foreground -m-1 p-1"
											aria-label="Why parts matter"
										>
											<InfoIcon class="size-3" />
										</button>
									{/snippet}
								</Popover.Trigger>
								<Popover.Content align="end" class="text-left font-normal">
									<p>
										ClickHouse stores each table's data in immutable parts and periodically merges them together in the
										background.
									</p>
									<p>
										A high active-part count means merges aren't keeping up - it slows queries (every part is a separate
										read) and usually points to small, frequent inserts rather than fewer, larger ones. Flare flags
										{HIGH_FRAGMENTATION_PARTS}+ active parts as high fragmentation - the same point at which ClickHouse
										itself starts throttling inserts to let merges catch up.
									</p>
								</Popover.Content>
							</Popover.Root>
						</span>
					</Table.Head>
					<Table.Head class="text-right">Compressed</Table.Head>
					<Table.Head class="text-right">Uncompressed</Table.Head>
					<Table.Head class="text-right">Ratio</Table.Head>
					<Table.Head class="text-right" title="Bytes added in the last 30 days, as a % of current compressed size">
						Growth
					</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each indexing.stats.tables as table (table.tableName)}
					{@const addedBytes = addedBytesByTable.get(table.tableName) ?? 0}
					{@const partsHealth = computePartsHealth(table.activeParts)}
					<Table.Row>
						<Table.Cell class="font-medium">{table.tableName}</Table.Cell>
						<Table.Cell><Badge variant="outline">{table.engine}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground max-w-64 truncate font-mono text-xs" title={table.sortingKey}>
							{table.sortingKey}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(table.rows)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							<span class="inline-flex items-center justify-end gap-1.5">
								{formatCount(table.activeParts)}
								<span
									class="flex items-center gap-1 text-xs font-normal {partsHealth.tone === 'warning'
										? 'text-warning'
										: 'text-muted-foreground'}"
								>
									{#if partsHealth.tone === 'warning'}
										<TriangleAlertIcon class="size-3 shrink-0" />
									{/if}
									{partsHealth.label}
								</span>
							</span>
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatBytes(table.compressedBytes)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatBytes(table.uncompressedBytes)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatRatio(table.compressedBytes, table.uncompressedBytes)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums {growthClass(addedBytes, table.compressedBytes)}">
							{indexing.stats.growthAvailable ? formatTableGrowth(addedBytes, table.compressedBytes) : '—'}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
