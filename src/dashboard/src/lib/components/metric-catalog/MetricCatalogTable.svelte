<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
	import ChartLineIcon from '@lucide/svelte/icons/chart-line';
	import { metricCatalogContext } from '$lib/metric-catalog/context';
	import type { MetricCatalogSortColumn } from '$lib/metric-catalog/state.svelte';
	import { formatCount } from '$lib/ingestion/format';
	import { formatDateTime } from '$lib/time/format';
	import { TYPE_LABEL, cardinalityClass, formatAgo } from './format';
	import * as m from '$lib/paraglide/messages';

	const catalog = metricCatalogContext.get();

	interface ColumnDef {
		column: MetricCatalogSortColumn | null;
		label: string;
		title?: string;
		align: 'left' | 'right';
	}

	const columns = $derived<ColumnDef[]>([
		{ column: 'metricName', label: m.metricCatalog_metricColumn(), align: 'left' },
		{ column: 'type', label: m.metricCatalog_typeColumn(), align: 'left' },
		{ column: null, label: m.metricCatalog_unitColumn(), align: 'left' },
		{ column: 'serviceCount', label: m.metricCatalog_servicesColumn(), align: 'right' },
		{ column: 'seriesCount', label: m.metricCatalog_seriesColumn(), title: m.metricCatalog_seriesTitle(), align: 'right' },
		{ column: 'sampleCount', label: m.metricCatalog_samplesColumn(), align: 'right' },
		{ column: 'lastReceivedMs', label: m.metricCatalog_lastReceivedColumn(), align: 'right' }
	]);
</script>

<div class="px-4 pb-4">
	{#if catalog.error}
		<p class="text-destructive py-2 text-xs">{catalog.error}</p>
	{/if}
	{#if catalog.loading && !catalog.metrics}
		<div class="flex h-32 items-center justify-center">
			<Spinner />
		</div>
	{:else if !catalog.metrics || catalog.metrics.length === 0}
		<Empty.Root>
			<Empty.Header>
				<Empty.Media variant="icon"><ChartLineIcon /></Empty.Media>
				<Empty.Title>{m.metricCatalog_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.metricCatalog_emptyDescription()}</Empty.Description>
			</Empty.Header>
		</Empty.Root>
	{:else}
		{#if catalog.truncated}
			<p class="text-warning py-2 text-xs">{m.metricCatalog_truncated({ count: catalog.metrics.length })}</p>
		{/if}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					{#each columns as col (col.label)}
						{@const active = col.column !== null && catalog.sortColumn === col.column}
						<Table.Head
							class={col.align === 'right' ? 'text-right' : ''}
							title={col.title}
							aria-sort={col.column === null ? undefined : active ? (catalog.sortDescending ? 'descending' : 'ascending') : 'none'}
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
									onclick={() => catalog.setSort(column)}
								>
									{col.label}
									{#if active}
										{#if catalog.sortDescending}
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
				{#each catalog.sorted() as metric (`${metric.metricName} ${metric.type}`)}
					<Table.Row>
						<Table.Cell class="max-w-md">
							<button
								type="button"
								class="block max-w-full truncate text-left font-medium hover:underline"
								onclick={() => catalog.openMetric(metric.metricName, metric.type)}
							>
								{metric.metricName}
							</button>
							{#if metric.description}
								<span class="text-muted-foreground block truncate text-xs" title={metric.description}>{metric.description}</span>
							{/if}
						</Table.Cell>
						<Table.Cell><Badge variant="outline">{TYPE_LABEL[metric.type]}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground">{metric.unit ?? '—'}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">{metric.serviceCount}</Table.Cell>
						<Table.Cell class="text-right tabular-nums {cardinalityClass(metric.seriesCount)}" title={metric.seriesCount.toLocaleString()}>
							{formatCount(metric.seriesCount)}
						</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={metric.sampleCount.toLocaleString()}>{formatCount(metric.sampleCount)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums" title={formatDateTime(metric.lastReceivedMs)}>
							{formatAgo(metric.lastReceivedMs, catalog.loadedAt)}
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
