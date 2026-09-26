<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
	import InboxIcon from '@lucide/svelte/icons/inbox';
	import { messagingContext } from '$lib/messaging/context';
	import { destinationErrorRate, type MessagingSortColumn } from '$lib/messaging/state.svelte';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDurationNano } from '$lib/traces/duration';
	import { formatRequestRate } from '$lib/services/format';
	import { formatCount } from '$lib/ingestion/format';
	import * as m from '$lib/paraglide/messages';

	const messaging = messagingContext.get();

	// Same two-tier escalation as ServicesTable's errorRateClass.
	function errorRateClass(rate: number): string {
		if (rate >= 0.05) return 'text-destructive font-medium';
		if (rate > 0) return 'text-warning';
		return '';
	}

	// Any Kafka row without lag means the collector's kafkametrics receiver isn't feeding it -
	// worth one hint under the table rather than a tooltip on every dash.
	const lagHint = $derived((messaging.destinations ?? []).some((d) => d.system === 'kafka' && d.consumerLag == null));

	interface ColumnDef {
		column: MessagingSortColumn | null;
		label: string;
		align: 'left' | 'right';
	}

	const columns = $derived<ColumnDef[]>([
		{ column: 'destination', label: m.messagingPage_destinationColumn(), align: 'left' },
		{ column: null, label: m.messagingPage_systemColumn(), align: 'left' },
		{ column: 'publishPerSecond', label: m.messagingPage_publishRateColumn(), align: 'right' },
		{ column: 'consumePerSecond', label: m.messagingPage_consumeRateColumn(), align: 'right' },
		{ column: 'errorRate', label: m.messagingPage_errorRateColumn(), align: 'right' },
		{ column: 'publishP99Ms', label: m.messagingPage_publishP99Column(), align: 'right' },
		{ column: 'consumeP99Ms', label: m.messagingPage_consumeP99Column(), align: 'right' },
		{ column: null, label: m.messagingPage_servicesColumn(), align: 'right' },
		{ column: 'consumerLag', label: m.messagingPage_lagColumn(), align: 'right' }
	]);
</script>

{#snippet dash(title: string)}
	<span class="text-muted-foreground" {title}>&mdash;</span>
{/snippet}

<div class="px-4 pb-4">
	{#if messaging.error}
		<p class="text-destructive py-2 text-xs">{messaging.error}</p>
	{/if}
	{#if messaging.loading && !messaging.destinations}
		<div class="flex h-32 items-center justify-center">
			<Spinner />
		</div>
	{:else if !messaging.destinations || messaging.destinations.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><InboxIcon /></Empty.Media>
				<Empty.Title>{m.messagingPage_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.messagingPage_emptyDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.label)}
						{@const active = col.column !== null && messaging.sortColumn === col.column}
						<Table.Head
							class={col.align === 'right' ? 'text-right' : ''}
							aria-sort={col.column === null ? undefined : active ? (messaging.sortDescending ? 'descending' : 'ascending') : 'none'}
						>
							{#if col.column === null}
								{col.label}
							{:else}
								{@const column = col.column}
								<button
									type="button"
									class="hover:text-foreground inline-flex items-center gap-1 {col.align === 'right' ? 'flex-row-reverse' : ''} {active
										? 'text-foreground'
										: ''}"
									onclick={() => messaging.setSort(column)}
								>
									{col.label}
									{#if active}
										{#if messaging.sortDescending}
											<ChevronDownIcon class="size-3" />
										{:else}
											<ChevronUpIcon class="size-3" />
										{/if}
									{:else}
										<ArrowUpDownIcon class="text-muted-foreground/50 size-3" />
									{/if}
								</button>
							{/if}
						</Table.Head>
					{/each}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each messaging.sorted() as row (row.system + '\u0000' + row.destination)}
					{@const errorRate = destinationErrorRate(row)}
					<Table.Row>
						<Table.Cell class="font-medium">
							<button type="button" class="hover:underline" onclick={() => messaging.open(row)}>
								{row.destination || m.messagingPage_unnamedDestination()}
							</button>
						</Table.Cell>
						<Table.Cell><Badge variant="outline">{row.system}</Badge></Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatCount(row.publishCount)}>
							{#if row.publishCount === 0}
								{@render dash(m.messagingPage_noPublishTooltip())}
							{:else}
								{m.servicesTable_requestRateValue({ rate: formatRequestRate(row.publishPerSecond) })}
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatCount(row.consumeCount)}>
							{#if row.consumeCount === 0}
								{@render dash(m.messagingPage_noConsumeTooltip())}
							{:else}
								{m.servicesTable_requestRateValue({ rate: formatRequestRate(row.consumePerSecond) })}
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums {errorRateClass(errorRate)}">{formatPercent(errorRate * 100)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if row.publishCount === 0}{@render dash(m.messagingPage_noPublishTooltip())}{:else}{formatDurationNano(row.publishP99Ms * 1_000_000)}{/if}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if row.consumeCount === 0}{@render dash(m.messagingPage_noConsumeTooltip())}{:else}{formatDurationNano(row.consumeP99Ms * 1_000_000)}{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-right tabular-nums">
							{m.messagingPage_servicesValue({ producers: row.producerServiceCount, consumers: row.consumerServiceCount })}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{#if row.consumerLag == null}
								{@render dash(m.messagingPage_noLagTooltip())}
							{:else}
								{formatCount(row.consumerLag)}
							{/if}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
		{#if lagHint}
			<p class="text-muted-foreground pt-3 text-xs">{m.messagingPage_lagHint()}</p>
		{/if}
	{/if}
</div>
