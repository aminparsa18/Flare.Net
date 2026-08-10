<script lang="ts">
	// Flare's analog to Seq's per-input row list - Flare has no named API-key "inputs"
	// (no auth exists anywhere yet), so the natural per-row unit here is (signal,
	// protocol): the six OTLP receivers Flare.Ingest actually terminates
	// (Logs/Traces/Metrics x gRPC/HTTP), matching what OtlpHttpLogsEndpoints and friends
	// individually instrument.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatBytes, formatCount } from '$lib/ingestion/format';
	import type { IngestionProtocol, IngestionSignal } from '$lib/ingestion-api';

	const ingestion = ingestionContext.get();

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
		// Fixed row order (Logs/Traces/Metrics x gRPC/HTTP), not insertion order - stable
		// across refreshes even as which combos have traffic changes.
		const order: [IngestionSignal, IngestionProtocol][] = [
			['Logs', 'Grpc'],
			['Logs', 'Http'],
			['Traces', 'Grpc'],
			['Traces', 'Http'],
			['Metrics', 'Grpc'],
			['Metrics', 'Http']
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
				<Empty.Title>No traffic in this window</Empty.Title>
				<Empty.Description>Point an OTLP exporter at :4317 (gRPC) or :4318 (HTTP) to see it here.</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>Receiver</Table.Head>
					<Table.Head class="text-right">Requests</Table.Head>
					<Table.Head class="text-right">Events</Table.Head>
					<Table.Head class="text-right">Bytes</Table.Head>
					<Table.Head class="text-right">Rejected</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each rows as row (row.signal + row.protocol)}
					<Table.Row>
						<Table.Cell class="flex items-center gap-2">
							<span class="font-medium">{row.signal}</span>
							<Badge variant="outline">{row.protocol === 'Grpc' ? 'gRPC :4317' : 'HTTP :4318'}</Badge>
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(row.requests)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(row.records)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatBytes(row.bytes)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums {row.rejected > 0 ? 'text-destructive' : ''}">
							{formatCount(row.rejected)}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
