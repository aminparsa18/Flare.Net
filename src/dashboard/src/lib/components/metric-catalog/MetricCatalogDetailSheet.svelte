<script lang="ts">
	import * as Sheet from '$lib/components/ui/sheet';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import ChartLineIcon from '@lucide/svelte/icons/chart-line';
	import { metricCatalogContext } from '$lib/metric-catalog/context';
	import { authContext } from '$lib/auth/context';
	import MetricInspectPanel from './MetricInspectPanel.svelte';
	import MetricAttributeRulesSection from './MetricAttributeRulesSection.svelte';
	import MetricDashboardUsageSection from './MetricDashboardUsageSection.svelte';
	import MetricMetadataOverridePopover from './MetricMetadataOverridePopover.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import { buildMetricsExplorerHref } from '$lib/deep-links';
	import { formatCount } from '$lib/ingestion/format';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDateTime } from '$lib/time/format';
	import { TYPE_LABEL, cardinalityClass, formatAgo } from './format';
	import * as m from '$lib/paraglide/messages';

	const catalog = metricCatalogContext.get();
	const auth = authContext.get();

	const detail = $derived(catalog.detail);

	// Series are already per (service, attribute set), so the per-service counts add up to the
	// metric's total - no second query for it.
	const totals = $derived.by(() => {
		const services = detail?.services ?? [];
		return {
			series: services.reduce((sum, s) => sum + s.seriesCount, 0),
			samples: services.reduce((sum, s) => sum + s.sampleCount, 0)
		};
	});

	function relatedEvidence(r: { sharedNamePrefix: string | null; sharedAttributeKeyCount: number; sharedServiceCount: number }): string {
		const parts: string[] = [];
		if (r.sharedNamePrefix) parts.push(m.metricCatalog_relatedPrefix({ prefix: r.sharedNamePrefix }));
		if (r.sharedAttributeKeyCount > 0) parts.push(m.metricCatalog_relatedKeys({ count: r.sharedAttributeKeyCount }));
		if (r.sharedServiceCount > 0) parts.push(m.metricCatalog_relatedServices({ count: r.sharedServiceCount }));
		return parts.join(' · ');
	}
</script>

<Sheet.Root
	open={catalog.selected !== null}
	onOpenChange={(next) => {
		if (!next) catalog.closeMetric();
	}}
>
	<Sheet.Content class="flex w-full flex-col data-[side=right]:sm:max-w-3xl">
		{#if catalog.selected}
			{@const selected = catalog.selected}
			<Sheet.Header>
				<Sheet.Title class="flex flex-wrap items-center gap-2 break-all">
					{selected.metricName}
					<Badge variant="outline">{TYPE_LABEL[selected.type]}</Badge>
					{#if detail?.unit}
						<Badge variant="secondary">{detail.unit}</Badge>
					{/if}
					{#if detail?.hasMetadataOverride}
						<Badge variant="outline" title={m.metricMetadata_overriddenTitle()}>{m.metricMetadata_overridden()}</Badge>
					{/if}
					{#if detail?.treatAsCounter && selected.type === 'Gauge'}
						<Badge variant="outline" title={m.metricMetadata_counterTitle()}>{m.metricMetadata_counter()}</Badge>
					{/if}
				</Sheet.Title>
				<Sheet.Description>
					{#if detail?.description}{`${detail.description} · `}{/if}{servicesWindowPresetLabel(catalog.windowPreset)}
				</Sheet.Description>
				<div class="flex flex-wrap items-center gap-2">
					<Button variant="outline" size="sm" href={buildMetricsExplorerHref(selected.metricName, selected.type, catalog.windowPreset)}>
						<ChartLineIcon data-icon="inline-start" />
						{m.metricCatalog_openInExplorer()}
					</Button>
					{#if auth.isAdmin && detail}
						<MetricMetadataOverridePopover
							unit={detail.unit}
							description={detail.description}
							emittedUnit={detail.emittedUnit}
							emittedDescription={detail.emittedDescription}
							hasOverride={detail.hasMetadataOverride}
							isGauge={selected.type === 'Gauge'}
							treatAsCounter={detail.treatAsCounter}
							onSave={(unit, description, treatAsCounter) => catalog.saveMetadataOverride(unit, description, treatAsCounter)}
							onReset={() => catalog.resetMetadataOverride()}
						/>
					{/if}
					{#if detail && detail.services.length > 0}
						<div class="ml-auto flex rounded-md border p-0.5" role="tablist">
							{#each [{ view: 'overview', label: m.metricCatalog_overviewTab() }, { view: 'inspect', label: m.metricCatalog_inspectTab() }] as const as tab (tab.view)}
								<Button
									variant={catalog.detailView === tab.view ? 'secondary' : 'ghost'}
									size="sm"
									class="h-7"
									role="tab"
									aria-selected={catalog.detailView === tab.view}
									onclick={() => catalog.setDetailView(tab.view)}
								>
									{tab.label}
								</Button>
							{/each}
						</div>
					{/if}
				</div>
			</Sheet.Header>
			<div class="min-h-0 flex-1 space-y-6 overflow-y-auto px-4 pb-8">
				{#if catalog.detailView === 'inspect' && detail && detail.services.length > 0}
					<MetricInspectPanel />
				{:else if catalog.detailLoading && !detail}
					<div class="flex h-32 items-center justify-center"><Spinner /></div>
				{:else if catalog.detailError}
					<p class="text-destructive text-sm">{catalog.detailError}</p>
				{:else if !detail || detail.services.length === 0}
					<p class="text-muted-foreground text-sm">{m.metricCatalog_noData()}</p>
				{:else}
					<dl class="grid grid-cols-3 gap-3">
						<div class="rounded-md border p-3">
							<dt class="text-muted-foreground text-xs">{m.metricCatalog_seriesColumn()}</dt>
							<dd class="text-lg tabular-nums {cardinalityClass(totals.series)}" title={totals.series.toLocaleString()}>{formatCount(totals.series)}</dd>
						</div>
						<div class="rounded-md border p-3">
							<dt class="text-muted-foreground text-xs">{m.metricCatalog_samplesColumn()}</dt>
							<dd class="text-lg tabular-nums" title={totals.samples.toLocaleString()}>{formatCount(totals.samples)}</dd>
						</div>
						<div class="rounded-md border p-3">
							<dt class="text-muted-foreground text-xs">{m.metricCatalog_servicesColumn()}</dt>
							<dd class="text-lg tabular-nums">{detail.services.length}</dd>
						</div>
					</dl>

					<section>
						<h3 class="text-sm font-medium">{m.metricCatalog_attributesHeading()}</h3>
						{#if detail.attributes.length === 0}
							<p class="text-muted-foreground mt-1 text-xs">{m.metricCatalog_noAttributes()}</p>
						{:else}
							<p class="text-muted-foreground mt-1 text-xs">{m.metricCatalog_attributesHint()}</p>
							<Table.Root>
								<Table.Header>
									<Table.Row>
										<Table.Head>{m.metricCatalog_attributeKeyColumn()}</Table.Head>
										<Table.Head class="text-right">{m.metricCatalog_distinctValuesColumn()}</Table.Head>
										<Table.Head class="text-right" title={m.metricCatalog_coverageTitle()}>{m.metricCatalog_coverageColumn()}</Table.Head>
										<Table.Head>{m.metricCatalog_topValuesColumn()}</Table.Head>
									</Table.Row>
								</Table.Header>
								<Table.Body>
									{#each detail.attributes as attribute (attribute.key)}
										<Table.Row>
											<Table.Cell class="font-mono text-xs">{attribute.key}</Table.Cell>
											<Table.Cell class="text-right tabular-nums {cardinalityClass(attribute.distinctValueCount)}">
												{formatCount(attribute.distinctValueCount)}
											</Table.Cell>
											<Table.Cell class="text-right tabular-nums">
												{formatPercent(totals.samples === 0 ? 0 : (attribute.sampleCount / totals.samples) * 100)}
											</Table.Cell>
											<Table.Cell>
												<div class="flex max-w-xs flex-wrap gap-1">
													{#each attribute.sampleValues as value, i (i)}
														<Badge variant="secondary" class="max-w-40 truncate font-mono" title={value}>
															{value === '' ? m.metricCatalog_emptyValue() : value}
														</Badge>
													{/each}
												</div>
											</Table.Cell>
										</Table.Row>
									{/each}
								</Table.Body>
							</Table.Root>
						{/if}
					</section>

					{#if detail.attributes.length > 0 || auth.canMutate}
						<MetricAttributeRulesSection metricName={selected.metricName} attributes={detail.attributes} canMutate={auth.canMutate} />
					{/if}

					<section>
						<h3 class="text-sm font-medium">{m.metricCatalog_servicesHeading()}</h3>
						<Table.Root>
							<Table.Header>
								<Table.Row>
									<Table.Head>{m.metricCatalog_serviceColumn()}</Table.Head>
									<Table.Head class="text-right">{m.metricCatalog_seriesColumn()}</Table.Head>
									<Table.Head class="text-right">{m.metricCatalog_samplesColumn()}</Table.Head>
									<Table.Head class="text-right">{m.metricCatalog_lastReceivedColumn()}</Table.Head>
								</Table.Row>
							</Table.Header>
							<Table.Body>
								{#each detail.services as service (service.serviceName)}
									<Table.Row>
										<Table.Cell>{service.serviceName || '—'}</Table.Cell>
										<Table.Cell class="text-right tabular-nums {cardinalityClass(service.seriesCount)}">{formatCount(service.seriesCount)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{formatCount(service.sampleCount)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums" title={formatDateTime(service.lastReceivedMs)}>
											{formatAgo(service.lastReceivedMs, catalog.loadedAt || Date.now())}
										</Table.Cell>
									</Table.Row>
								{/each}
							</Table.Body>
						</Table.Root>
					</section>

					<MetricDashboardUsageSection metricName={selected.metricName} />

					<section>
						<h3 class="text-sm font-medium">{m.metricCatalog_relatedHeading()}</h3>
						{#if detail.related.length === 0}
							<p class="text-muted-foreground mt-1 text-xs">{m.metricCatalog_noRelated()}</p>
						{:else}
							<ul class="mt-2 divide-y rounded-md border">
								{#each detail.related as related (`${related.metricName} ${related.type}`)}
									<li class="flex flex-wrap items-center gap-2 px-3 py-2">
										<button
											type="button"
											class="truncate text-left text-sm font-medium hover:underline"
											onclick={() => catalog.openMetric(related.metricName, related.type)}
										>
											{related.metricName}
										</button>
										<Badge variant="outline">{TYPE_LABEL[related.type]}</Badge>
										<span class="text-muted-foreground ml-auto text-xs">{relatedEvidence(related)}</span>
									</li>
								{/each}
							</ul>
						{/if}
					</section>
				{/if}
			</div>
		{/if}
	</Sheet.Content>
</Sheet.Root>
