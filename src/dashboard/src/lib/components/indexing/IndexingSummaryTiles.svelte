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
	// or one unusually heavy day alone would be a noisy headline number.
	const GROWTH_WINDOW_DAYS = 7;
	const growthPerDay = $derived.by(() => {
		const stats = indexing.stats;
		if (!stats?.growthAvailable || stats.growth.length === 0) return null;
		const days = [...new Set(stats.growth.map((p) => p.day))].sort();
		const window = days.slice(-GROWTH_WINDOW_DAYS);
		if (window.length === 0) return null;
		const windowSet = new Set(window);
		const totalBytes = stats.growth.filter((p) => windowSet.has(p.day)).reduce((sum, p) => sum + p.bytes, 0);
		return { bytesPerDay: totalBytes / window.length, windowDays: window.length };
	});

	const queryPerformance = $derived(indexing.stats?.queryPerformance);
	const p95Class = $derived.by(() => {
		const p95Ms = queryPerformance?.p95Ms;
		if (p95Ms === null || p95Ms === undefined) return '';
		if (p95Ms >= 1000) return 'text-destructive';
		if (p95Ms >= 300) return 'text-warning';
		return '';
	});
</script>

<div class="grid grid-cols-2 gap-3 p-4 lg:grid-cols-4">
	<Card.Root>
		<Card.Header>
			<Card.Description>Storage</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatBytes(totals.totalCompressed)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">
			{#if diskUsedPercent !== null && diskUsage}
				<span class={diskUsedClass}>
					{formatBytes(totals.totalCompressed)} / {formatBytes(diskUsage.totalBytes)} · {formatPercent(diskUsedPercent)} used
				</span>
			{:else}
				{formatBytes(totals.totalUncompressed)} uncompressed
			{/if}
		</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>Rows</Card.Description>
			<Card.Title class="text-2xl tabular-nums">{formatCount(totals.totalRows)}</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">across {totals.tableCount} tables with data</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>Ingestion growth</Card.Description>
			<Card.Title class="flex items-baseline gap-1 text-2xl tabular-nums">
				{#if growthPerDay}
					+{formatBytes(growthPerDay.bytesPerDay)}<span class="text-muted-foreground text-xs font-normal">/day</span>
				{:else}
					—
				{/if}
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">
			{#if !indexing.stats?.growthAvailable}
				Not available - <code class="font-mono">system.part_log</code> isn't queryable
			{:else if growthPerDay}
				avg over last {growthPerDay.windowDays} day{growthPerDay.windowDays === 1 ? '' : 's'}
			{:else}
				no new data in the last 30 days
			{/if}
		</Card.Content>
	</Card.Root>

	<Card.Root>
		<Card.Header>
			<Card.Description>Query performance</Card.Description>
			<Card.Title class="flex items-baseline gap-1 text-2xl tabular-nums {p95Class}">
				{#if queryPerformance?.available && queryPerformance.p95Ms !== null}
					{formatMs(queryPerformance.p95Ms)}<span class="text-muted-foreground text-xs font-normal">p95</span>
				{:else}
					—
				{/if}
			</Card.Title>
		</Card.Header>
		<Card.Content class="text-muted-foreground text-xs">
			{#if !queryPerformance?.available}
				Not available - <code class="font-mono">system.query_log</code> isn't queryable
			{:else if queryPerformance.sampleCount === 0}
				no queries in the last {queryPerformance.windowMinutes}m
			{:else}
				past {queryPerformance.windowMinutes}m · {formatCount(queryPerformance.sampleCount)} quer{queryPerformance.sampleCount === 1
					? 'y'
					: 'ies'}
			{/if}
		</Card.Content>
	</Card.Root>
</div>
