<script lang="ts">
	// Redesigned per feedback: the original four cards (storage/rows/tables/skip indexes)
	// were all "what do I have," not "what should I care about" - tables and skip-index
	// counts especially, since every index here is schema-defined at migration time (see
	// IndexingQueryService's doc comment), not something a self-hosted user creates or
	// tunes, so a bare count of them never changes and never signals a problem. Swapped
	// the back two for ingestion growth and query performance - both actionable, both
	// already the kind of thing an operator would reach for clickhouse-client to check.
	// Storage keeps its spot but grows a disk-headroom line: self-hosted deployments have
	// no "configured limit" the way a SaaS plan would, so system.disks' real total/free
	// stands in for one instead of a made-up quota.
	import * as Card from '$lib/components/ui/card';
	import { indexingContext } from '$lib/indexing/context';
	import { formatBytes, formatCount, formatMs, formatPercent } from '$lib/indexing/format';
	import { latencyClass } from '$lib/indexing/health';
	import { averageDailyGrowth } from '$lib/indexing/growth';
	import * as m from '$lib/paraglide/messages';

	const indexing = indexingContext.get();

	const totals = $derived.by(() => {
		const tables = indexing.stats?.tables ?? [];
		return {
			tableCount: tables.filter((t) => t.rows > 0).length,
			totalRows: tables.reduce((sum, t) => sum + t.rows, 0),
			totalCompressed: tables.reduce((sum, t) => sum + t.compressedBytes, 0),
			totalUncompressed: tables.reduce((sum, t) => sum + t.uncompressedBytes, 0)
		};
	});

	const diskUsage = $derived(indexing.stats?.diskUsage);
	const diskUsedPercent = $derived.by(() => {
		if (!diskUsage?.available || diskUsage.totalBytes <= 0) return null;
		return ((diskUsage.totalBytes - diskUsage.freeBytes) / diskUsage.totalBytes) * 100;
	});
	const diskUsedClass = $derived.by(() => {
		if (diskUsedPercent === null) return '';
		if (diskUsedPercent >= 90) return 'text-destructive';
		if (diskUsedPercent >= 75) return 'text-warning';
		return '';
	});

	// "Ingestion growth" averages new-part bytes/day over the trailing week of the same
	// 30-day series the growth chart plots, rather than just yesterday's total - one quiet
	// or one unusually heavy day alone would be a noisy headline number. Shared with the
	// growth chart's own aggregation and Storage health's cross-signal check - see
	// $lib/indexing/growth.ts.
	const growthPerDay = $derived.by(() => {
		const stats = indexing.stats;
		if (!stats?.growthAvailable || stats.growth.length === 0) return null;
		return averageDailyGrowth(stats.growth);
	});

	const queryPerformance = $derived(indexing.stats?.queryPerformance);
</script>

<div class="grid grid-cols-2 gap-3 p-4 lg:grid-cols-4">
	<Card.Root>
		<Card.Header>
			<Card.Description>{m.indexingSummaryTiles_storage()}</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatBytes(totals.totalCompressed)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">
			{#if diskUsedPercent !== null && diskUsage}
				<span class={diskUsedClass}>
					{m.indexingSummaryTiles_usedSummary({
						compressed: formatBytes(totals.totalCompressed),
						total: formatBytes(diskUsage.totalBytes),
						percent: formatPercent(diskUsedPercent)
					})}
				</span>
			{:else}
				{m.indexingSummaryTiles_uncompressedSuffix({ uncompressed: formatBytes(totals.totalUncompressed) })}
			{/if}
		</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.indexingSummaryTiles_rows()}</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatCount(totals.totalRows)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">{m.indexingSummaryTiles_acrossTables({ count: totals.tableCount })}</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.indexingSummaryTiles_ingestionGrowth()}</Card.Description>
			<Card.Title class="flex items-baseline gap-1 text-2xl tabular-nums">
				{#if growthPerDay}
					+{formatBytes(growthPerDay.bytesPerDay)}<span class="text-muted-foreground text-xs font-normal"
						>{m.indexingSummaryTiles_perDayUnit()}</span
					>
				{:else}
					—
				{/if}
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">
			{#if !indexing.stats?.growthAvailable}
				{@html m.indexingCommon_notQueryable({ table: '<code class="font-mono">system.part_log</code>' })}
			{:else if growthPerDay}
				{growthPerDay.windowDays === 1
					? m.indexingSummaryTiles_avgOverLastDaySingular()
					: m.indexingSummaryTiles_avgOverLastDaysPlural({ days: growthPerDay.windowDays })}
			{:else}
				{m.indexingSummaryTiles_noNewDataIn30Days()}
			{/if}
		</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>{m.indexingSummaryTiles_queryPerformance()}</Card.Description>
			<Card.Title class="flex items-baseline gap-1 text-2xl tabular-nums {latencyClass(queryPerformance?.p95Ms)}">
				{#if queryPerformance?.available && queryPerformance.p95Ms !== null}
					{formatMs(queryPerformance.p95Ms)}<span class="text-muted-foreground text-xs font-normal">p95</span>
				{:else}
					—
				{/if}
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">
			{#if !queryPerformance?.available}
				{@html m.indexingCommon_notQueryable({ table: '<code class="font-mono">system.query_log</code>' })}
			{:else if queryPerformance.sampleCount === 0}
				{m.indexingSummaryTiles_noQueriesInWindow({ minutes: queryPerformance.windowMinutes })}
			{:else}
				{queryPerformance.sampleCount === 1
					? m.indexingSummaryTiles_pastWindowSingular({ minutes: queryPerformance.windowMinutes, count: formatCount(queryPerformance.sampleCount) })
					: m.indexingSummaryTiles_pastWindowPlural({ minutes: queryPerformance.windowMinutes, count: formatCount(queryPerformance.sampleCount) })}
			{/if}
		</Card.Content>
	</Card.Root>
</div>
