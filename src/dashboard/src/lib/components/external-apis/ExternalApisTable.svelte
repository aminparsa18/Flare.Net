<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import GlobeIcon from '@lucide/svelte/icons/globe';
	import SortableHead from './SortableHead.svelte';
	import { externalApisContext } from '$lib/external-apis/context';
	import { errorRate, type ExternalDomainSortColumn } from '$lib/external-apis/state.svelte';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDurationNano } from '$lib/traces/duration';
	import { formatRequestRate } from '$lib/services/format';
	import { formatCount } from '$lib/ingestion/format';
	import { formatAgo } from '$lib/components/metric-catalog/format';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const externalApis = externalApisContext.get();

	// Same two-tier escalation as MessagingTable's errorRateClass.
	function errorRateClass(rate: number): string {
		if (rate >= 0.05) return 'text-destructive font-medium';
		if (rate > 0) return 'text-warning';
		return '';
	}

	// Captured per load, not ticking - "last seen" is relative to when the data was fetched.
	const nowMs = $derived(externalApis.domains ? Date.now() : 0);

	const columns = $derived<{ column: ExternalDomainSortColumn; label: string; align: 'left' | 'right' }[]>([
		{ column: 'domain', label: m.externalApisPage_domainColumn(), align: 'left' },
		{ column: 'perSecond', label: m.externalApisPage_rateColumn(), align: 'right' },
		{ column: 'errorRate', label: m.externalApisPage_errorRateColumn(), align: 'right' },
		{ column: 'p95Ms', label: m.externalApisPage_p95Column(), align: 'right' },
		{ column: 'p99Ms', label: m.externalApisPage_p99Column(), align: 'right' },
		{ column: 'endpointCount', label: m.externalApisPage_endpointsColumn(), align: 'right' },
		{ column: 'lastSeenUnixMs', label: m.externalApisPage_lastSeenColumn(), align: 'right' }
	]);
</script>

<div class="px-4 pb-4">
	{#if externalApis.error}
		<p class="text-destructive py-2 text-xs">{externalApis.error}</p>
	{/if}
	{#if externalApis.loading && !externalApis.domains}
		<div class="flex h-32 items-center justify-center">
			<Spinner />
		</div>
	{:else if !externalApis.domains || externalApis.domains.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><GlobeIcon /></Empty.Media>
				<Empty.Title>{m.externalApisPage_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.externalApisPage_emptyDescription()}</Empty.Description>
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
							active={externalApis.sortColumn === col.column}
							descending={externalApis.sortDescending}
							onSort={() => externalApis.setSort(col.column)}
						/>
						{#if col.column === 'domain'}
							<Table.Head>{m.externalApisPage_portColumn()}</Table.Head>
						{/if}
					{/each}
					<Table.Head class="text-right">{m.externalApisPage_servicesColumn()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each externalApis.sorted() as row (row.domain)}
					{@const rate = errorRate(row)}
					<Table.Row>
						<Table.Cell class="font-medium">
							<button type="button" class="hover:underline" onclick={() => externalApis.open(row)}>{row.domain}</button>
						</Table.Cell>
						<Table.Cell class="text-muted-foreground tabular-nums">{row.ports || '\u2014'}</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatCount(row.callCount)}>
							{m.servicesTable_requestRateValue({ rate: formatRequestRate(row.perSecond) })}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums {errorRateClass(rate)}">{formatPercent(rate * 100)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatDurationNano(row.p95Ms * 1_000_000)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatDurationNano(row.p99Ms * 1_000_000)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatCount(row.endpointCount)}</Table.Cell>
						<Table.Cell class="text-muted-foreground text-right tabular-nums" title={formatDateTime(row.lastSeenUnixMs)}>
							{formatAgo(row.lastSeenUnixMs, nowMs)}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-right tabular-nums">{formatCount(row.serviceCount)}</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
		<p class="text-muted-foreground pt-3 text-xs">{m.externalApisPage_hint()}</p>
	{/if}
</div>
