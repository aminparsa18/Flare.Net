<script lang="ts">
	// Flare's analog to Seq's per-input row list - Flare has no named API-key "inputs"
	// (no auth exists anywhere yet), so the natural per-row unit here is (signal,
	// protocol): the six OTLP receivers Flare.Ingest actually terminates
	// (Logs/Traces/Metrics x gRPC/HTTP), matching what OtlpHttpLogsEndpoints and friends
	// individually instrument, plus a 7th row for the pull-based Prometheus scrape
	// receiver (Metrics-only - Logs/Traces never scrape, so this isn't a full 3rd signal
	// column the way gRPC/HTTP are).
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatBytes, formatCount, signalLabel } from '$lib/ingestion/format';
	import type { IngestionProtocol, IngestionSignal } from '$lib/ingestion-api';
	import RejectedTelemetryDialog from './RejectedTelemetryDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	// A function, not a static `.label` field - see time-range.ts's own remarks on why a
	// module-scope const can't reflect a per-request/live-switched locale.
	function protocolBadge(protocol: IngestionProtocol): string {
		switch (protocol) {
			case 'Grpc':
				return m.ingestionSignalsTable_grpcLabel();
			case 'Http':
				return m.ingestionSignalsTable_httpLabel();
			case 'Scrape':
				return m.ingestionSignalsTable_scrapeLabel();
		}
	}

	const ingestion = ingestionContext.get();

	// Which row's Rejected count opened the drill-down, if any - one dialog instance shared
	// across rows rather than one per row, same "single controlled dialog" shape
	// ExportDialog uses. dialogRow is left set after the dialog first closes (only `open`
	// toggles) rather than nulled out - harmless, since it's not rendered while closed, and
	// avoids re-deriving it on every close.
	let dialogOpen = $state(false);
	let dialogRow = $state<{ signal: IngestionSignal; protocol: IngestionProtocol; rejected: number } | null>(null);

	interface Row {
		signal: IngestionSignal;
		protocol: IngestionProtocol;
		requests: number;
		records: number;
		bytes: number;
		rejected: number;
	}

	const rows = $derived.by((): Row[] => {
		const buckets = ingestion.stats?.buckets ?? [];
		const key = (s: IngestionSignal, p: IngestionProtocol) => `${s}:${p}`;
		const totals = new Map<string, Row>();
		for (const b of buckets) {
			const k = key(b.signal, b.protocol);
			const existing = totals.get(k) ?? { signal: b.signal, protocol: b.protocol, requests: 0, records: 0, bytes: 0, rejected: 0 };
			existing.requests += b.requests;
			existing.records += b.records;
			existing.bytes += b.bytes;
			existing.rejected += b.rejected;
			totals.set(k, existing);
		}
		// Fixed row order (Logs/Traces/Metrics x gRPC/HTTP, plus Metrics/Scrape), not
		// insertion order - stable across refreshes even as which combos have traffic
		// changes.
		const order: [IngestionSignal, IngestionProtocol][] = [
			['Logs', 'Grpc'],
			['Logs', 'Http'],
			['Traces', 'Grpc'],
			['Traces', 'Http'],
			['Metrics', 'Grpc'],
			['Metrics', 'Http'],
			['Metrics', 'Scrape']
		];
		return order.map(
			([signal, protocol]) => totals.get(key(signal, protocol)) ?? { signal, protocol, requests: 0, records: 0, bytes: 0, rejected: 0 }
		);
	});
</script>

<div class="px-4 pb-4">
	{#if ingestion.stats && rows.every((r) => r.requests === 0)}
		<Empty.Root>
			<Empty.Header>
				<Empty.Title>{m.ingestionSignalsTable_noTrafficTitle()}</Empty.Title>
				<Empty.Description>{m.ingestionSignalsTable_noTrafficDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.ingestionSignalsTable_receiverColumn()}</Table.Head>
					<Table.Head class="text-right">{m.ingestionSignalsTable_requestsColumn()}</Table.Head>
					<Table.Head class="text-right">{m.ingestionSignalsTable_eventsColumn()}</Table.Head>
					<Table.Head class="text-right">{m.ingestionSignalsTable_bytesColumn()}</Table.Head>
					<Table.Head class="text-right">{m.ingestionSignalsTable_rejectedColumn()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each rows as row (row.signal + row.protocol)}
					<Table.Row>
						<Table.Cell class="flex items-center gap-2">
							<span class="font-medium">{signalLabel(row.signal)}</span>
							<Badge variant="outline">{protocolBadge(row.protocol)}</Badge>
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(row.requests)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(row.records)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatBytes(row.bytes)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if row.rejected > 0}
								<button
									type="button"
									class="text-destructive cursor-pointer underline decoration-dotted underline-offset-2 hover:decoration-solid"
									onclick={() => {
										dialogRow = { signal: row.signal, protocol: row.protocol, rejected: row.rejected };
										dialogOpen = true;
									}}
								>
									{formatCount(row.rejected)}
								</button>
							{:else}
								{formatCount(row.rejected)}
							{/if}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>

{#if dialogRow}
	<RejectedTelemetryDialog
		bind:open={dialogOpen}
		signal={dialogRow.signal}
		protocol={dialogRow.protocol}
		rejectedCount={dialogRow.rejected}
	/>
{/if}
