<script lang="ts">
	// Feedback: the page already shows "gRPC :4317"/"HTTP :4318" as badges on every row of
	// IngestionSignalsTable, but never as a receiver-level rollup of its own - this is that
	// rollup, one row per protocol summed across all three signals for the selected window.
	// Zero requests reads as Idle, not a problem - "nobody's using OTLP/HTTP" is a completely
	// normal deployment shape (gRPC-only exporters are the common case), not something worth
	// coloring the same as an actual failure. Reuses computeReceiverStatus (ingestion/
	// health.ts) and the same status icon/tone vocabulary PipelineFlushHealthTable already
	// established, rather than a third slightly-different status treatment on this page.
	import * as Table from '$lib/components/ui/table';
	import { ingestionContext } from '$lib/ingestion/context';
	import { formatCount } from '$lib/ingestion/format';
	import { computeReceiverStatus, type FlushStatusTone } from '$lib/ingestion/health';
	import type { IngestionProtocol } from '$lib/ingestion-api';
	import CheckIcon from '@lucide/svelte/icons/check';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import CircleXIcon from '@lucide/svelte/icons/circle-x';
	import MinusIcon from '@lucide/svelte/icons/minus';

	const ingestion = ingestionContext.get();

	const PROTOCOLS: { value: IngestionProtocol; label: string }[] = [
		{ value: 'Grpc', label: 'gRPC :4317' },
		{ value: 'Http', label: 'HTTP :4318' },
		{ value: 'Scrape', label: 'Prometheus scrape' }
	];

	const rows = $derived.by(() => {
		const buckets = ingestion.stats?.buckets ?? [];
		return PROTOCOLS.map(({ value, label }) => {
			const matching = buckets.filter((b) => b.protocol === value);
			const requests = matching.reduce((sum, b) => sum + b.requests, 0);
			const rejected = matching.reduce((sum, b) => sum + b.rejected, 0);
			return { protocol: value, label, requests, rejected, status: computeReceiverStatus(requests, rejected) };
		});
	});

	const TONE_TEXT_CLASS = {
		good: 'text-emerald-600 dark:text-emerald-400',
		default: 'text-muted-foreground',
		warning: 'text-warning',
		destructive: 'text-destructive'
	} satisfies Record<FlushStatusTone, string>;

	const TONE_ICON = {
		good: CheckIcon,
		default: MinusIcon,
		warning: TriangleAlertIcon,
		destructive: CircleXIcon
	} satisfies Record<FlushStatusTone, typeof CheckIcon>;
</script>

<div class="px-4 pb-4">
	<h2 class="mb-2 text-sm font-medium">Receivers</h2>
	<Table.Root>
		<Table.Header>
			<Table.Row>
				<Table.Head>Receiver</Table.Head>
				<Table.Head>Status</Table.Head>
				<Table.Head class="text-right">Requests</Table.Head>
			</Table.Row>
		</Table.Header>
		<Table.Body>
			{#each rows as row (row.protocol)}
				{@const StatusIcon = TONE_ICON[row.status.tone]}
				<Table.Row>
					<Table.Cell class="font-medium">{row.label}</Table.Cell>
					<Table.Cell>
						<span class="flex items-center gap-1 {TONE_TEXT_CLASS[row.status.tone]}">
							<StatusIcon class="size-3.5 shrink-0" />
							{row.status.label}
						</span>
					</Table.Cell>
					<Table.Cell class="text-right tabular-nums">{formatCount(row.requests)} req</Table.Cell>
				</Table.Row>
			{/each}
		</Table.Body>
	</Table.Root>
</div>