<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
	import ActivityIcon from '@lucide/svelte/icons/activity';
	import { servicesContext } from '$lib/services/context';
	import type { ServicesSortColumn } from '$lib/services/state.svelte';
	import { formatMs, formatPercent } from '$lib/indexing/format';
	import { formatRequestRate } from '$lib/services/format';
	import { buildTracesDeepLinkHref } from '$lib/deep-links';
	import * as m from '$lib/paraglide/messages';

	const services = servicesContext.get();

	// >=5% error rate reads as "broken", >=1% as "worth a look" - same two-tier
	// warning/destructive escalation IndexingTablesTable's growthClass already uses for
	// this codebase's tables, just with thresholds picked for an error-rate stat instead
	// of a storage-growth one.
	function errorRateClass(errorRate: number): string {
		if (errorRate >= 0.05) return 'text-destructive font-medium';
		if (errorRate >= 0.01) return 'text-warning';
		return '';
	}

	interface ColumnDef {
		column: ServicesSortColumn;
		label: string;
		align: 'left' | 'right';
	}

	const columns = $derived<ColumnDef[]>([
		{ column: 'serviceName', label: m.servicesTable_serviceColumn(), align: 'left' },
		{ column: 'requestsPerSecond', label: m.servicesTable_requestRateColumn(), align: 'right' },
		{ column: 'errorRate', label: m.servicesTable_errorRateColumn(), align: 'right' },
		{ column: 'p50DurationMs', label: m.servicesTable_p50Column(), align: 'right' },
		{ column: 'p95DurationMs', label: m.servicesTable_p95Column(), align: 'right' },
		{ column: 'p99DurationMs', label: m.servicesTable_p99Column(), align: 'right' }
	]);
</script>

<div class="px-4 pb-4">
	{#if services.loading && !services.services}
		<div class="flex h-32 items-center justify-center">
			<Spinner />
		</div>
	{:else if !services.services || services.services.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><ActivityIcon /></Empty.Media>
				<Empty.Title>{m.servicesTable_noServicesTitle()}</Empty.Title>
				<Empty.Description>{m.servicesTable_noServicesDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.column)}
						{@const active = services.sortColumn === col.column}
						<Table.Head
							class={col.align === 'right' ? 'text-right' : ''}
							aria-sort={active ? (services.sortDescending ? 'descending' : 'ascending') : 'none'}
						>
							<button
								type="button"
								class="hover:text-foreground inline-flex items-center gap-1 {col.align === 'right' ? 'flex-row-reverse' : ''} {active
									? 'text-foreground'
									: ''}"
								onclick={() => services.setSort(col.column)}
							>
								{col.label}
								{#if active}
									{#if services.sortDescending}
										<ChevronDownIcon class="size-3" />
									{:else}
										<ChevronUpIcon class="size-3" />
									{/if}
								{:else}
									<ArrowUpDownIcon class="text-muted-foreground/50 size-3" />
								{/if}
							</button>
						</Table.Head>
					{/each}
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each services.sorted() as service (service.serviceName)}
					<Table.Row>
						<Table.Cell class="font-medium">
							<a
								class="hover:underline"
								href={buildTracesDeepLinkHref({ serviceName: service.serviceName, timeRangePreset: services.windowPreset })}
							>
								{service.serviceName}
							</a>
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{m.servicesTable_requestRateValue({ rate: formatRequestRate(service.requestsPerSecond) })}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums {errorRateClass(service.errorRate)}">
							{formatPercent(service.errorRate * 100)}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatMs(service.p50DurationMs)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatMs(service.p95DurationMs)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{formatMs(service.p99DurationMs)}</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
