<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import SparklesIcon from '@lucide/svelte/icons/sparkles';
	import SortableHead from '$lib/components/external-apis/SortableHead.svelte';
	import { llmContext } from '$lib/llm/context';
	import { authContext } from '$lib/auth/context';
	import LlmPricePopover from './LlmPricePopover.svelte';
	import { errorRate, type LlmSortColumn } from '$lib/llm/state.svelte';
	import { buildLlmCallTracesHref } from '$lib/deep-links';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDurationNano } from '$lib/traces/duration';
	import { formatRequestRate } from '$lib/services/format';
	import { formatCount } from '$lib/ingestion/format';
	import { formatAgo } from '$lib/components/metric-catalog/format';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const llm = llmContext.get();
	const auth = authContext.get();

	/** USD; small spends keep their cents, large ones drop them. */
	function formatCost(usd: number): string {
		if (usd > 0 && usd < 0.01) return '<$0.01';
		return new Intl.NumberFormat(undefined, {
			style: 'currency',
			currency: 'USD',
			maximumFractionDigits: usd >= 100 ? 0 : 2
		}).format(usd);
	}

	// Same two-tier escalation as ExternalApisTable's errorRateClass.
	function errorRateClass(rate: number): string {
		if (rate >= 0.05) return 'text-destructive font-medium';
		if (rate > 0) return 'text-warning';
		return '';
	}

	// Captured per load, not ticking - "last seen" is relative to when the data was fetched.
	const nowMs = $derived(llm.models ? Date.now() : 0);

	const columns = $derived<{ column: LlmSortColumn; label: string; align: 'left' | 'right' }[]>([
		{ column: 'model', label: m.llmPage_modelColumn(), align: 'left' },
		{ column: 'perSecond', label: m.llmPage_rateColumn(), align: 'right' },
		{ column: 'errorRate', label: m.llmPage_errorRateColumn(), align: 'right' },
		{ column: 'p95Ms', label: m.llmPage_p95Column(), align: 'right' },
		{ column: 'p99Ms', label: m.llmPage_p99Column(), align: 'right' },
		{ column: 'inputTokens', label: m.llmPage_inputTokensColumn(), align: 'right' },
		{ column: 'outputTokens', label: m.llmPage_outputTokensColumn(), align: 'right' },
		{ column: 'estimatedCost', label: m.llmPage_costColumn(), align: 'right' },
		{ column: 'lastSeenUnixMs', label: m.llmPage_lastSeenColumn(), align: 'right' }
	]);
</script>

<div class="px-4 pb-4">
	{#if llm.error}
		<p class="text-destructive py-2 text-xs">{llm.error}</p>
	{/if}
	{#if llm.loading && !llm.models}
		<div class="flex h-32 items-center justify-center">
			<Spinner />
		</div>
	{:else if !llm.models || llm.models.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><SparklesIcon /></Empty.Media>
				<Empty.Title>{m.llmPage_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.llmPage_emptyDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.column)}
						<SortableHead
							label={col.label}
							align={col.align}
							active={llm.sortColumn === col.column}
							descending={llm.sortDescending}
							onSort={() => llm.setSort(col.column)}
						/>
						{#if col.column === 'model'}
							<Table.Head>{m.llmPage_providerColumn()}</Table.Head>
						{/if}
					{/each}
					<Table.Head class="text-right">{m.llmPage_servicesColumn()}</Table.Head>
					<Table.Head class="text-right">{m.llmPage_tracesColumn()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each llm.sorted() as row (row.provider + '\0' + row.model)}
					{@const rate = errorRate(row)}
					<Table.Row>
						<Table.Cell class="font-medium">{row.model || '—'}</Table.Cell>
						<Table.Cell class="text-muted-foreground">{row.provider || '—'}</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatCount(row.callCount)}>
							{m.servicesTable_requestRateValue({ rate: formatRequestRate(row.perSecond) })}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums {errorRateClass(rate)}">{formatPercent(rate * 100)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatDurationNano(row.p95Ms * 1_000_000)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatDurationNano(row.p99Ms * 1_000_000)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(row.inputTokens)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(row.outputTokens)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							<span class="inline-flex items-center justify-end gap-1">
								{#if row.estimatedCost != null}
									<span
										title="{row.inputPricePerMillion} / {row.outputPricePerMillion} {m.llmPage_priceTitle()}{row.priceIsCustom ? ` · ${m.llmPage_priceCustom()}` : ''}"
									>
										{formatCost(row.estimatedCost)}
									</span>
								{:else}
									<span class="text-muted-foreground">{m.llmPage_costUnpriced()}</span>
								{/if}
								{#if auth.isAdmin && row.model}
									<LlmPricePopover
										model={row.model}
										inputPrice={row.inputPricePerMillion}
										outputPrice={row.outputPricePerMillion}
										isCustom={row.priceIsCustom}
										onSave={(input, output) => llm.savePrice(row.model, input, output)}
										onReset={() => llm.resetPrice(row.model)}
									/>
								{/if}
							</span>
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-right tabular-nums" title={formatDateTime(row.lastSeenUnixMs)}>
							{formatAgo(row.lastSeenUnixMs, nowMs)}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-right tabular-nums">{formatCount(row.serviceCount)}</Table.Cell>
						<Table.Cell class="text-right">
							{#if row.model}
								<a class="text-muted-foreground text-xs hover:underline" href={buildLlmCallTracesHref({ model: row.model, service: llm.service }, llm.windowPreset)}>
									{m.llmPage_viewTraces()}
								</a>
							{/if}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
		<p class="text-muted-foreground pt-3 text-xs">{m.llmPage_hint()}</p>
	{/if}
</div>
