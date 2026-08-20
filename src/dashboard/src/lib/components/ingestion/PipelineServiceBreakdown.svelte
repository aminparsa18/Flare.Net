<script lang="ts">
	// Per-service.name arrivals/bytes breakdown (Planning.md v10) - folded in from the
	// original Ingestion page proposal (Planning.md's own "Later" note). One compact table
	// per signal that actually has traffic, capped to the top N services
	// (PipelineQueryService.TopServicesPerSignal) with an "+N more" row for the rest - same
	// convention MetricChart/IndexingGrowthChart already use for a bounded legend.
	//
	// Feedback follow-ups:
	// - Rate (records/sec, using the page's own selected window) and each service's %
	//   share of its signal's total - a bare event count doesn't say whether a service is
	//   dominant or a rounding error next to the others.
	// - "Top N services" heading only appears once there actually *is* a longer tail
	//   (otherServiceCount > 0) - misleading to say "Top 5" when the 5 shown are all there
	//   are. No "View all" link: unlike the "+N more" row (an exact count/sum the backend
	//   already computes, PipelineQueryService.BuildServiceBreakdown), an unbounded
	//   per-service list has no backing endpoint anywhere in this app yet - adding one is a
	//   bigger lift than this table's own scope, so this stays honest about what's here
	//   instead of linking to something that doesn't exist.
	// - Service names are clickable for Logs/Traces, landing pre-filtered in that signal's
	//   Explorer via the same deep-link mechanism MetricChart's "View related logs"/"View
	//   traces" actions already use ($lib/deep-links.ts) - reused wholesale, not
	//   reinvented. Metrics has no equivalent target: MetricPicker selects a specific
	//   (metricName, serviceName) pair, and this table only knows the service, not which
	//   metric - so Metrics service names stay plain text rather than linking to a picker
	//   that still can't land on anything without a metric name to go with it.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatBytes, formatCount } from '$lib/ingestion/format';
	import { INGESTION_WINDOW_PRESETS } from '$lib/ingestion/state.svelte';
	import { buildLogsDeepLinkHref, buildTracesDeepLinkHref } from '$lib/deep-links';
	import type { TimeRangePreset } from '$lib/logs/time-range';
	import type { IngestionSignal } from '$lib/ingestion-api';
	import type { PipelineServiceEntry } from '$lib/pipeline-api';

	const ingestion = ingestionContext.get();

	const breakdowns = $derived(
		(ingestion.pipeline?.serviceBreakdowns ?? []).filter((b) => b.topServices.length > 0 || b.otherServiceCount > 0)
	);

	const windowSeconds = $derived(
		(INGESTION_WINDOW_PRESETS.find((p) => p.value === ingestion.windowPreset)?.minutes ?? 60) * 60
	);

	function formatRate(records: number): string {
		return `${formatCount(records / windowSeconds)}/s`;
	}

	function sharePercent(records: number, total: number): number | null {
		return total > 0 ? Math.round((records / total) * 100) : null;
	}

	// IngestionWindowPreset ('15m'|'1h'|'6h'|'24h') is an exact-value subset of
	// TimeRangePreset - see deep-links.ts's ParsedLogsDeepLink/ParsedTracesDeepLink, which
	// both accept exactly those four plus '7d'/'custom' that this page never produces.
	function serviceHref(signal: IngestionSignal, serviceName: string): string | null {
		const timeRangePreset = ingestion.windowPreset as TimeRangePreset;
		if (signal === 'Logs') return buildLogsDeepLinkHref({ serviceName, timeRangePreset });
		if (signal === 'Traces') return buildTracesDeepLinkHref({ serviceName, timeRangePreset });
		return null;
	}
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
				{@const total = breakdown.topServices.reduce((sum: number, e: PipelineServiceEntry) => sum + e.records, 0) + breakdown.otherRecords}
				<div>
					<h3 class="text-muted-foreground mb-1 text-xs font-medium">
						{breakdown.signal}{#if breakdown.otherServiceCount > 0}&nbsp;· Top {breakdown.topServices.length} services{/if}
					</h3>
					<Table.Root>
						<Table.Header>
							<Table.Row>
								<Table.Head>Service</Table.Head>
								<Table.Head class="text-right">Events</Table.Head>
								<Table.Head class="text-right">Rate</Table.Head>
								<Table.Head class="text-right">Bytes</Table.Head>
							</Table.Row>
						</Table.Header>
						<Table.Body>
							{#each breakdown.topServices as entry (entry.serviceName)}
								{@const href = serviceHref(breakdown.signal, entry.serviceName)}
								{@const pct = sharePercent(entry.records, total)}
								<Table.Row>
									<Table.Cell class="max-w-32 truncate font-mono text-xs" title={entry.serviceName}>
										{#if href}
											<a {href} class="hover:text-foreground hover:underline">{entry.serviceName}</a>
										{:else}
											{entry.serviceName}
										{/if}
									</Table.Cell>
									<Table.Cell class="text-right tabular-nums">
										{formatCount(entry.records)}
										{#if pct !== null}<span class="text-muted-foreground">({pct}%)</span>{/if}
									</Table.Cell>
									<Table.Cell class="text-muted-foreground text-right tabular-nums">{formatRate(entry.records)}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{formatBytes(entry.bytes)}</Table.Cell>
								</Table.Row>
							{/each}
							{#if breakdown.otherServiceCount > 0}
								<Table.Row>
									<Table.Cell class="text-muted-foreground text-xs">+{breakdown.otherServiceCount} more</Table.Cell>
									<Table.Cell class="text-muted-foreground text-right tabular-nums">{formatCount(breakdown.otherRecords)}</Table.Cell>
									<Table.Cell class="text-muted-foreground text-right tabular-nums">{formatRate(breakdown.otherRecords)}</Table.Cell>
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