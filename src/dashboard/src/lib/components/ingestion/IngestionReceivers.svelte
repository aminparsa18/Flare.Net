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
	import * as m from '$lib/paraglide/messages';

	const ingestion = ingestionContext.get();

	// A function, not a static `.label` field - see time-range.ts's own remarks on why a
	// module-scope const can't reflect a per-request/live-switched locale.
	function receiverLabel(protocol: IngestionProtocol): string {
		switch (protocol) {
			case 'Grpc':
				return m.ingestionReceivers_grpcLabel();
			case 'Http':
				return m.ingestionReceivers_httpLabel();
			case 'Scrape':
				return m.ingestionReceivers_scrapeLabel();
		}
	}
	const PROTOCOLS: IngestionProtocol[] = ['Grpc', 'Http', 'Scrape'];

	const rows = $derived.by(() => {
		const buckets = ingestion.stats?.buckets ?? [];
		return PROTOCOLS.map((value) => {
			const matching = buckets.filter((b) => b.protocol === value);
			const requests = matching.reduce((sum, b) => sum + b.requests, 0);
			const rejected = matching.reduce((sum, b) => sum + b.rejected, 0);
			return { protocol: value, label: receiverLabel(value), requests, rejected, status: computeReceiverStatus(requests, rejected) };
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
	<h2 class="mb-2 text-sm font-medium">{m.ingestionReceivers_heading()}</h2>
	<Table.Root>
		<Table.Header>
			<Table.Row>
				<Table.Head>{m.ingestionReceivers_receiverColumn()}</Table.Head>
				<Table.Head>{m.ingestionReceivers_statusColumn()}</Table.Head>
				<Table.Head class="text-right">{m.ingestionReceivers_requestsColumn()}</Table.Head>
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
					<Table.Cell class="text-right tabular-nums">{m.ingestionReceivers_requestsValue({ count: formatCount(row.requests) })}</Table.Cell>
				</Table.Row>
			{/each}
		</Table.Body>
	</Table.Root>
</div>