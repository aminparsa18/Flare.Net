<script lang="ts">
	// Replaces the old "Skip indexes" section's framing. Feedback: a bare inventory of
	// index names ("idx_log_attr_key", "idx_res_attr_value", ...) answers "what indexes
	// exist," a developer's question - the person running Flare.Net actually wants "are my
	// searches fast." Reframed as Query optimization: search latency (p50/p95/p99) and a
	// slow-query count answer that question directly, from the same system.query_log data
	// the "Query performance" summary card already reads. The index inventory (now under
	// "Indexes", IndexingSkipIndexesTable below) is still here - just demoted to detail
	// underneath the question it actually helps answer, instead of being the whole page.
	//
	// Deliberately does NOT include an "index effectiveness" stat (e.g. "94% of indexed
	// queries benefited from data skipping") - see Planning.md's "Later" section for why:
	// ClickHouse doesn't expose that as reliable, always-on telemetry the way query_log's
	// duration/count already are, and a made-up number would be worse than none.
	import * as Card from '$lib/components/ui/card';
	import { indexingContext } from '$lib/indexing/context';
	import { formatCount, formatMs } from '$lib/indexing/format';
	import { latencyClass } from '$lib/indexing/health';

	const indexing = indexingContext.get();

	const queryPerformance = $derived(indexing.stats?.queryPerformance);

	const PERCENTILES = [
		{ key: 'p50Ms', label: 'p50' },
		{ key: 'p95Ms', label: 'p95' },
		{ key: 'p99Ms', label: 'p99' }
	] as const;

	const indexSummary = $derived.by(() => {
		const indexes = indexing.stats?.skipIndexes ?? [];
		return {
			indexCount: indexes.length,
			tableCount: new Set(indexes.map((i) => i.tableName)).size
		};
	});
</script>

<div class="flex flex-col gap-3 px-4 pb-4">
	<h2 class="text-sm font-medium">Query optimization</h2>

	<div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
		<Card.Root>
			<Card.Header>
				<Card.Description>Search latency</Card.Description>
			</Card.Header>
			<Card.Content class="flex flex-col gap-1.5">
				{#if !queryPerformance?.available}
					<p class="text-muted-foreground text-xs">
						Not available - <code class="font-mono">system.query_log</code> isn't queryable
					</p>
				{:else if queryPerformance.sampleCount === 0}
					<p class="text-muted-foreground text-xs">no queries in the last {queryPerformance.windowMinutes}m</p>
				{:else}
					{#each PERCENTILES as { key, label } (key)}
						{@const value = queryPerformance[key]}
						<div class="flex items-center justify-between text-sm">
							<span class="text-muted-foreground">{label}</span>
							<span class="tabular-nums font-medium {latencyClass(value)}">
								{value === null ? '—' : formatMs(value)}
							</span>
						</div>
					{/each}
				{/if}
			</Card.Content>
		</Card.Root>

		<Card.Root>
			<Card.Header>
				<Card.Description>Slow queries</Card.Description>
				<Card.Title class="text-2xl tabular-nums {(queryPerformance?.slowQueryCount ?? 0) > 0 ? 'text-warning' : ''}">
					{queryPerformance?.available ? formatCount(queryPerformance.slowQueryCount) : '—'}
				</Card.Title>
			</Card.Header>
			<Card.Content class="text-muted-foreground text-xs">
				{#if !queryPerformance?.available}
					Not available - <code class="font-mono">system.query_log</code> isn't queryable
				{:else}
					queries over {queryPerformance.slowQueryThresholdMs} ms, past {queryPerformance.windowMinutes}m
				{/if}
			</Card.Content>
		</Card.Root>
	</div>

	<p class="text-muted-foreground pt-1 text-sm">
		<span class="text-foreground font-medium">{formatCount(indexSummary.indexCount)}</span> indexes across
		<span class="text-foreground font-medium">{formatCount(indexSummary.tableCount)}</span> tables
	</p>
</div>
