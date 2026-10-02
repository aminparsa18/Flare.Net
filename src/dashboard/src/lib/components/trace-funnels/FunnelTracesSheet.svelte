<script lang="ts">
	import { withBase } from '$lib/paths';
	// The traces behind one step's reached/dropped/errored figure - most recent first, capped
	// server-side (TraceFunnelQueryBuilder.MaxTraces). Each row opens the trace waterfall.
	import * as Sheet from '$lib/components/ui/sheet';
	import * as Table from '$lib/components/ui/table';
	import { Spinner } from '$lib/components/ui/spinner';
	import { traceFunnelContext } from '$lib/trace-funnels/context';
	import { stepLabel } from '$lib/trace-funnels/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import { formatDurationNano } from '$lib/traces/duration';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const funnel = traceFunnelContext.get();

	const drill = $derived(funnel.drill);
	const stepCount = $derived(funnel.resultSteps.length);

	function title(): string {
		if (drill == null) return '';
		const number = drill.stepIndex + 1;
		switch (drill.outcome) {
			case 'Reached':
				return m.funnelsPage_drillReached({ number });
			case 'Dropped':
				return m.funnelsPage_drillDropped({ number, next: number + 1 });
			case 'Errored':
				return m.funnelsPage_drillErrored({ number });
		}
	}
</script>

<Sheet.Root
	open={drill !== null}
	onOpenChange={(next) => {
		if (!next) funnel.closeDrill();
	}}
>
	<Sheet.Content class="flex w-full flex-col data-[side=right]:sm:max-w-2xl">
		{#if drill}
			<Sheet.Header>
				<Sheet.Title>{title()}</Sheet.Title>
				<Sheet.Description>
					{stepLabel(funnel.resultSteps[drill.stepIndex])} · {servicesWindowPresetLabel(funnel.windowPreset)}
				</Sheet.Description>
			</Sheet.Header>
			<div class="min-h-0 flex-1 overflow-y-auto px-4 pb-8">
				{#if funnel.drillError}
					<p class="text-destructive text-sm">{funnel.drillError}</p>
				{:else if funnel.drillTraces == null}
					<div class="text-muted-foreground flex items-center gap-2 text-sm"><Spinner class="size-4" />{m.funnelsPage_running()}</div>
				{:else if funnel.drillTraces.length === 0}
					<p class="text-muted-foreground text-sm">{m.funnelsPage_drillEmpty()}</p>
				{:else}
					<Table.Root>
						<Table.Header>
							<Table.Row>
								<Table.Head>{m.funnelsPage_traceColumn()}</Table.Head>
								<Table.Head>{m.funnelsPage_startedColumn()}</Table.Head>
								<Table.Head class="text-right">{m.funnelsPage_reachedColumn()}</Table.Head>
								<Table.Head class="text-right" title={m.funnelsPage_elapsedTitle()}>{m.funnelsPage_elapsedColumn()}</Table.Head>
							</Table.Row>
						</Table.Header>
						<Table.Body>
							{#each funnel.drillTraces as trace (trace.traceId)}
								<Table.Row>
									<Table.Cell class="font-mono text-xs">
										<a class="hover:underline" href={withBase(`/traces/${encodeURIComponent(trace.traceId)}`)}>{trace.traceId}</a>
									</Table.Cell>
									<Table.Cell class="text-xs whitespace-nowrap">{formatDateTime(trace.startUnixMs)}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{trace.reachedSteps} / {stepCount}</Table.Cell>
									<Table.Cell class="text-right tabular-nums">{formatDurationNano(trace.elapsedMs * 1_000_000)}</Table.Cell>
								</Table.Row>
							{/each}
						</Table.Body>
					</Table.Root>
					{#if funnel.drillTraces.length >= 100}
						<p class="text-muted-foreground mt-2 text-xs">{m.funnelsPage_drillCapped({ count: 100 })}</p>
					{/if}
				{/if}
			</div>
		{/if}
	</Sheet.Content>
</Sheet.Root>
