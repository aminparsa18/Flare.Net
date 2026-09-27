<script lang="ts">
	// The last run's per-step figures. Traces, errors and drop-off counts open the matching
	// drill-down (FunnelTracesSheet). Labels come from TraceFunnelState.resultSteps - the
	// steps this result was computed for - not the live editor.
	import * as Table from '$lib/components/ui/table';
	import { Spinner } from '$lib/components/ui/spinner';
	import { traceFunnelContext } from '$lib/trace-funnels/context';
	import { stepLabel } from '$lib/trace-funnels/state.svelte';
	import type { TraceFunnelOutcome } from '$lib/trace-funnels-api';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDurationNano } from '$lib/traces/duration';
	import { formatCount } from '$lib/ingestion/format';
	import * as m from '$lib/paraglide/messages';

	const funnel = traceFunnelContext.get();

	const result = $derived(funnel.result);
	const entered = $derived(result?.steps[0]?.traceCount ?? 0);

	function percentOf(count: number, total: number): string {
		return total === 0 ? '—' : formatPercent((count / total) * 100);
	}

	function ms(value: number): string {
		return formatDurationNano(value * 1_000_000);
	}
</script>

{#snippet drillLink(count: number, stepIndex: number, outcome: TraceFunnelOutcome, extraClass: string)}
	{#if count > 0}
		<button type="button" class="tabular-nums hover:underline {extraClass}" onclick={() => funnel.openDrill(stepIndex, outcome)} title={count.toLocaleString()}>
			{formatCount(count)}
		</button>
	{:else}
		<span class="text-muted-foreground tabular-nums">0</span>
	{/if}
{/snippet}

<section class="px-4 py-3" aria-live="polite">
	{#if funnel.error}
		<p class="text-destructive text-sm">{funnel.error}</p>
	{:else if result == null}
		{#if funnel.loading}
			<div class="text-muted-foreground flex items-center gap-2 text-sm"><Spinner class="size-4" />{m.funnelsPage_running()}</div>
		{:else}
			<p class="text-muted-foreground text-sm">{m.funnelsPage_emptyState()}</p>
		{/if}
	{:else if entered === 0}
		<p class="text-muted-foreground text-sm">{m.funnelsPage_noTraces()}</p>
	{:else}
		<p class="text-muted-foreground mb-2 text-sm">
			{m.funnelsPage_summary({ entered: entered.toLocaleString(), completed: percentOf(result.steps[result.steps.length - 1].traceCount, entered) })}
		</p>
		<Table.Root class={funnel.loading ? 'opacity-60' : ''}>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.funnelsPage_stepColumn()}</Table.Head>
					<Table.Head class="w-[28%]">{m.funnelsPage_tracesColumn()}</Table.Head>
					<Table.Head class="text-right">{m.funnelsPage_conversionColumn()}</Table.Head>
					<Table.Head class="text-right">{m.funnelsPage_droppedColumn()}</Table.Head>
					<Table.Head class="text-right">{m.funnelsPage_errorsColumn()}</Table.Head>
					<Table.Head class="text-right" title={m.funnelsPage_transitionTitle()}>{m.funnelsPage_p50Column()}</Table.Head>
					<Table.Head class="text-right" title={m.funnelsPage_transitionTitle()}>{m.funnelsPage_p95Column()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each result.steps as row, index (index)}
					{@const next = result.steps[index + 1]}
					{@const previous = result.steps[index - 1]}
					<Table.Row>
						<Table.Cell class="max-w-72">
							<div class="flex items-center gap-2">
								<span class="bg-muted flex size-5 shrink-0 items-center justify-center rounded-full text-[11px] tabular-nums">{index + 1}</span>
								<span class="truncate" title={stepLabel(funnel.resultSteps[index])}>{stepLabel(funnel.resultSteps[index])}</span>
							</div>
						</Table.Cell>
						<Table.Cell>
							<div class="flex items-center gap-2">
								<div class="bg-muted h-4 flex-1 overflow-hidden rounded-sm" aria-hidden="true">
									<div class="bg-primary h-full rounded-sm" style="width: {(row.traceCount / entered) * 100}%"></div>
								</div>
								<span class="w-24 shrink-0 text-right">
									{@render drillLink(row.traceCount, index, 'Reached', '')}
									<span class="text-muted-foreground text-xs">({percentOf(row.traceCount, entered)})</span>
								</span>
							</div>
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{previous ? percentOf(row.traceCount, previous.traceCount) : '—'}</Table.Cell>
						<Table.Cell class="text-right">
							{#if next}
								{@render drillLink(row.traceCount - next.traceCount, index, 'Dropped', '')}
							{:else}
								<span class="text-muted-foreground">—</span>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right">{@render drillLink(row.errorCount, index, 'Errored', 'text-destructive')}</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={previous ? m.funnelsPage_avgTitle({ value: ms(row.avgTransitionMs) }) : undefined}>
							{previous ? ms(row.p50TransitionMs) : '—'}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={previous ? m.funnelsPage_p99Title({ value: ms(row.p99TransitionMs) }) : undefined}>
							{previous ? ms(row.p95TransitionMs) : '—'}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</section>
