<script lang="ts">
	import * as Sheet from '$lib/components/ui/sheet';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { messagingContext } from '$lib/messaging/context';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import { buildTracesDeepLinkHref } from '$lib/deep-links';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDurationNano } from '$lib/traces/duration';
	import { formatRequestRate } from '$lib/services/format';
	import { formatBytes, formatCount } from '$lib/ingestion/format';
	import type { MessagingServiceStats } from '$lib/messaging-api';
	import * as m from '$lib/paraglide/messages';

	const messaging = messagingContext.get();

	const detail = $derived(messaging.detail);
	const hasGroups = $derived((detail?.consumers ?? []).some((c) => c.consumerGroup !== ''));
	// Confluent's producer instrumentation doesn't put the partition on its send spans, so
	// publish traffic per partition is often unknowable - hide the column rather than show
	// a column of zeros that reads as "nothing was published".
	const partitionPublishKnown = $derived((detail?.partitions ?? []).some((p) => p.publishCount > 0));
	const totalLag = $derived((detail?.consumerLag ?? []).reduce((sum, l) => sum + l.lag, 0));
	const totalDepth = $derived((detail?.queueDepth ?? []).reduce((sum, q) => sum + q.ready + q.unacknowledged, 0));
	// Only worth a column when the same queue name exists in more than one vhost.
	const showVhost = $derived(new Set((detail?.queueDepth ?? []).map((q) => q.vhost)).size > 1);

	function rate(perSecond: number): string {
		return m.servicesTable_requestRateValue({ rate: formatRequestRate(perSecond) });
	}
</script>

{#snippet serviceTable(rows: MessagingServiceStats[], showGroup: boolean, emptyLabel: string)}
	{#if rows.length === 0}
		<p class="text-muted-foreground text-sm">{emptyLabel}</p>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.messagingPage_serviceColumn()}</Table.Head>
					{#if showGroup}
						<Table.Head>{m.messagingPage_consumerGroupColumn()}</Table.Head>
					{/if}
					<Table.Head class="text-right">{m.messagingPage_rateColumn()}</Table.Head>
					<Table.Head class="text-right">{m.messagingPage_errorRateColumn()}</Table.Head>
					<Table.Head class="text-right">{m.messagingPage_p50Column()}</Table.Head>
					<Table.Head class="text-right">{m.messagingPage_p99Column()}</Table.Head>
					<Table.Head class="text-right">{m.messagingPage_avgSizeColumn()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each rows as row (row.serviceName + '\u0000' + row.consumerGroup)}
					<Table.Row>
						<Table.Cell class="font-medium">
							<a class="hover:underline" href={buildTracesDeepLinkHref({ serviceName: row.serviceName, timeRangePreset: messaging.windowPreset })}>
								{row.serviceName}
							</a>
						</Table.Cell>
						{#if showGroup}
							<Table.Cell>{row.consumerGroup || '—'}</Table.Cell>
						{/if}
						<Table.Cell class="text-right tabular-nums" title={formatCount(row.count)}>{rate(row.perSecond)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums {row.errorCount > 0 ? 'text-destructive' : ''}">
							{formatPercent((row.errorCount / row.count) * 100)}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatDurationNano(row.p50Ms * 1_000_000)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatDurationNano(row.p99Ms * 1_000_000)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{row.avgMessageBytes == null ? '—' : formatBytes(row.avgMessageBytes)}</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
{/snippet}

<Sheet.Root
	open={messaging.selected !== null}
	onOpenChange={(next) => {
		if (!next) messaging.close();
	}}
>
	<Sheet.Content class="flex w-full flex-col data-[side=right]:sm:max-w-3xl">
		{#if messaging.selected}
			<Sheet.Header>
				<Sheet.Title class="flex flex-wrap items-center gap-2">
					{messaging.selected.destination || m.messagingPage_unnamedDestination()}
					<Badge variant="outline">{messaging.selected.system}</Badge>
				</Sheet.Title>
				<Sheet.Description>
					{servicesWindowPresetLabel(messaging.windowPreset)}{messaging.service ? ` · ${messaging.service}` : ''}
				</Sheet.Description>
			</Sheet.Header>
			<div class="min-h-0 flex-1 space-y-6 overflow-y-auto px-4 pb-8">
				{#if messaging.detailLoading && !detail}
					<div class="flex h-32 items-center justify-center"><Spinner /></div>
				{:else if messaging.detailError}
					<p class="text-destructive text-sm">{messaging.detailError}</p>
				{:else if detail}
					<section class="space-y-2">
						<h2 class="text-sm font-medium">{m.messagingPage_producersHeading()}</h2>
						{@render serviceTable(detail.producers, false, m.messagingPage_noProducers())}
					</section>

					<section class="space-y-2">
						<h2 class="text-sm font-medium">{m.messagingPage_consumersHeading()}</h2>
						{@render serviceTable(detail.consumers, hasGroups, m.messagingPage_noConsumers())}
					</section>

					{#if detail.partitions.length > 0}
						<section class="space-y-2">
							<h2 class="text-sm font-medium">{m.messagingPage_partitionsHeading()}</h2>
							<Table.Root>
								<Table.Header>
									<Table.Row>
										<Table.Head>{m.messagingPage_partitionColumn()}</Table.Head>
										{#if partitionPublishKnown}
											<Table.Head class="text-right">{m.messagingPage_publishRateColumn()}</Table.Head>
										{/if}
										<Table.Head class="text-right">{m.messagingPage_consumeRateColumn()}</Table.Head>
										<Table.Head class="text-right">{m.messagingPage_errorsColumn()}</Table.Head>
									</Table.Row>
								</Table.Header>
								<Table.Body>
									{#each detail.partitions as p (p.partition)}
										<Table.Row>
											<Table.Cell class="tabular-nums">{p.partition}</Table.Cell>
											{#if partitionPublishKnown}
												<Table.Cell class="text-right tabular-nums" title={formatCount(p.publishCount)}>{rate(p.publishPerSecond)}</Table.Cell>
											{/if}
											<Table.Cell class="text-right tabular-nums" title={formatCount(p.consumeCount)}>{rate(p.consumePerSecond)}</Table.Cell>
											<Table.Cell class="text-right tabular-nums {p.errorCount > 0 ? 'text-destructive' : ''}">{formatCount(p.errorCount)}</Table.Cell>
										</Table.Row>
									{/each}
								</Table.Body>
							</Table.Root>
						</section>
					{/if}

					{#if detail.system === 'kafka'}
						<section class="space-y-2">
							<h2 class="text-sm font-medium">
								{m.messagingPage_lagHeading()}
								{#if detail.consumerLag.length > 0}
									<span class="text-muted-foreground font-normal"> · {m.messagingPage_lagTotal({ lag: formatCount(totalLag) })}</span>
								{/if}
							</h2>
							{#if detail.consumerLag.length === 0}
								<p class="text-muted-foreground text-sm">{m.messagingPage_lagHint()}</p>
							{:else}
								<Table.Root>
									<Table.Header>
										<Table.Row>
											<Table.Head>{m.messagingPage_consumerGroupColumn()}</Table.Head>
											<Table.Head>{m.messagingPage_partitionColumn()}</Table.Head>
											<Table.Head class="text-right">{m.messagingPage_lagColumn()}</Table.Head>
										</Table.Row>
									</Table.Header>
									<Table.Body>
										{#each detail.consumerLag as l (l.consumerGroup + '\u0000' + l.partition)}
											<Table.Row>
												<Table.Cell>{l.consumerGroup}</Table.Cell>
												<Table.Cell class="tabular-nums">{l.partition}</Table.Cell>
												<Table.Cell class="text-right tabular-nums">{formatCount(l.lag)}</Table.Cell>
											</Table.Row>
										{/each}
									</Table.Body>
								</Table.Root>
							{/if}
						</section>
					{/if}

					{#if detail.system === 'rabbitmq'}
						<section class="space-y-2">
							<h2 class="text-sm font-medium">
								{m.messagingPage_queueDepthHeading()}
								{#if detail.queueDepth.length > 0}
									<span class="text-muted-foreground font-normal"> · {m.messagingPage_lagTotal({ lag: formatCount(totalDepth) })}</span>
								{/if}
							</h2>
							{#if detail.queueDepth.length === 0}
								<p class="text-muted-foreground text-sm">{m.messagingPage_queueDepthHint()}</p>
							{:else}
								<Table.Root>
									<Table.Header>
										<Table.Row>
											<Table.Head>{m.messagingPage_queueColumn()}</Table.Head>
											{#if showVhost}
												<Table.Head>{m.messagingPage_vhostColumn()}</Table.Head>
											{/if}
											<Table.Head class="text-right">{m.messagingPage_readyColumn()}</Table.Head>
											<Table.Head class="text-right">{m.messagingPage_unackedColumn()}</Table.Head>
										</Table.Row>
									</Table.Header>
									<Table.Body>
										{#each detail.queueDepth as q (q.vhost + '\u0000' + q.queue)}
											<Table.Row>
												<Table.Cell>{q.queue}</Table.Cell>
												{#if showVhost}
													<Table.Cell>{q.vhost}</Table.Cell>
												{/if}
												<Table.Cell class="text-right tabular-nums">{formatCount(q.ready)}</Table.Cell>
												<Table.Cell class="text-right tabular-nums">{formatCount(q.unacknowledged)}</Table.Cell>
											</Table.Row>
										{/each}
									</Table.Body>
								</Table.Root>
							{/if}
						</section>
					{/if}
				{/if}
			</div>
		{/if}
	</Sheet.Content>
</Sheet.Root>
