<script lang="ts">
	// One domain's drill-down: status codes, endpoints, top errors and the services calling
	// it. Every row links to /traces narrowed to the matching calls - see
	// buildExternalCallTracesHref for how an endpoint turns back into a span filter.
	import * as Sheet from '$lib/components/ui/sheet';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import SortableHead from './SortableHead.svelte';
	import { externalApisContext } from '$lib/external-apis/context';
	import { errorRate, type ExternalEndpointSortColumn } from '$lib/external-apis/state.svelte';
	import { servicesWindowPresetLabel } from '$lib/services/state.svelte';
	import { buildExternalCallTracesHref, type ExternalCallTarget } from '$lib/deep-links';
	import { formatPercent } from '$lib/indexing/format';
	import { formatDurationNano } from '$lib/traces/duration';
	import { formatRequestRate } from '$lib/services/format';
	import { formatCount } from '$lib/ingestion/format';
	import { formatAgo } from '$lib/components/metric-catalog/format';
	import { formatDateTime } from '$lib/time/format';
	import * as m from '$lib/paraglide/messages';

	const externalApis = externalApisContext.get();

	const detail = $derived(externalApis.detail);
	const nowMs = $derived(detail ? Date.now() : 0);
	const totalStatusCalls = $derived((detail?.statusCodes ?? []).reduce((sum, s) => sum + s.callCount, 0));

	function tracesHref(target: Omit<ExternalCallTarget, 'domain' | 'service'>): string {
		return buildExternalCallTracesHref({ domain: externalApis.selected?.domain ?? '', service: externalApis.service, ...target }, externalApis.windowPreset);
	}

	function rate(perSecond: number): string {
		return m.servicesTable_requestRateValue({ rate: formatRequestRate(perSecond) });
	}

	function ms(value: number): string {
		return formatDurationNano(value * 1_000_000);
	}

	function statusVariant(code: string): 'destructive' | 'secondary' | 'outline' {
		if (code.startsWith('5')) return 'destructive';
		if (code.startsWith('4')) return 'secondary';
		return 'outline';
	}

	const endpointColumns = $derived<{ column: ExternalEndpointSortColumn; label: string; align: 'left' | 'right' }[]>([
		{ column: 'endpoint', label: m.externalApisPage_endpointColumn(), align: 'left' },
		{ column: 'perSecond', label: m.externalApisPage_rateColumn(), align: 'right' },
		{ column: 'errorRate', label: m.externalApisPage_errorRateColumn(), align: 'right' },
		{ column: 'p50Ms', label: m.externalApisPage_p50Column(), align: 'right' },
		{ column: 'p95Ms', label: m.externalApisPage_p95Column(), align: 'right' },
		{ column: 'p99Ms', label: m.externalApisPage_p99Column(), align: 'right' },
		{ column: 'lastSeenUnixMs', label: m.externalApisPage_lastSeenColumn(), align: 'right' }
	]);
</script>

{#snippet endpointLabel(method: string, endpoint: string)}
	{#if method}<span class="text-muted-foreground mr-1.5 font-mono text-xs">{method}</span>{/if}<span class="font-mono text-xs break-all">{endpoint}</span>
{/snippet}

<Sheet.Root
	open={externalApis.selected !== null}
	onOpenChange={(next) => {
		if (!next) externalApis.close();
	}}
>
	<Sheet.Content class="flex w-full flex-col data-[side=right]:sm:max-w-4xl">
		{#if externalApis.selected}
			<Sheet.Header>
				<Sheet.Title class="flex flex-wrap items-center gap-2">
					{externalApis.selected.domain}
					<a class="text-muted-foreground text-xs font-normal hover:underline" href={tracesHref({})}>{m.externalApisPage_viewTraces()}</a>
				</Sheet.Title>
				<Sheet.Description>
					{servicesWindowPresetLabel(externalApis.windowPreset)}{externalApis.service ? ` · ${externalApis.service}` : ''}
				</Sheet.Description>
			</Sheet.Header>
			<div class="min-h-0 flex-1 space-y-6 overflow-y-auto px-4 pb-8">
				{#if externalApis.detailLoading && !detail}
					<div class="flex h-32 items-center justify-center"><Spinner /></div>
				{:else if externalApis.detailError}
					<p class="text-destructive text-sm">{externalApis.detailError}</p>
				{:else if detail}
					<section class="space-y-2">
						<h2 class="text-sm font-medium">{m.externalApisPage_statusCodesHeading()}</h2>
						{#if detail.statusCodes.length === 0}
							<p class="text-muted-foreground text-sm">{m.externalApisPage_noStatusCodes()}</p>
						{:else}
							<div class="flex flex-wrap gap-2">
								{#each detail.statusCodes as s (s.statusCode)}
									<a href={tracesHref({ statusCode: s.statusCode })} title={m.externalApisPage_viewTraces()}>
										<Badge variant={statusVariant(s.statusCode)} class="tabular-nums">
											{s.statusCode} · {formatCount(s.callCount)}
											<span class="opacity-70">({formatPercent((s.callCount / totalStatusCalls) * 100)})</span>
										</Badge>
									</a>
								{/each}
							</div>
						{/if}
					</section>

					<section class="space-y-2">
						<h2 class="text-sm font-medium">{m.externalApisPage_endpointsHeading()}</h2>
						{#if detail.endpoints.length === 0}
							<p class="text-muted-foreground text-sm">{m.externalApisPage_noEndpoints()}</p>
						{:else}
							<Table.Root>
								<Table.Header>
									<Table.Row>
										{#each endpointColumns as col (col.column)}
											<SortableHead
												label={col.label}
												align={col.align}
												active={externalApis.endpointSortColumn === col.column}
												descending={externalApis.endpointSortDescending}
												onSort={() => externalApis.setEndpointSort(col.column)}
											/>
										{/each}
									</Table.Row>
								</Table.Header>
								<Table.Body>
									{#each externalApis.sortedEndpoints() as e (e.method + '\u0000' + e.endpoint + '\u0000' + e.endpointSource)}
										{@const eRate = errorRate(e)}
										<Table.Row>
											<Table.Cell>
												<a class="hover:underline" href={tracesHref({ method: e.method, endpoint: e.endpoint, endpointSource: e.endpointSource })}>
													{@render endpointLabel(e.method, e.endpoint)}
												</a>
											</Table.Cell>
											<Table.Cell class="text-right tabular-nums" title={formatCount(e.callCount)}>{rate(e.perSecond)}</Table.Cell>
											<Table.Cell class="text-right tabular-nums {eRate > 0 ? 'text-destructive' : ''}">
												{#if e.errorCount > 0}
													<a
														class="hover:underline"
														href={tracesHref({ method: e.method, endpoint: e.endpoint, endpointSource: e.endpointSource, errorsOnly: true })}
													>
														{formatPercent(eRate * 100)}
													</a>
												{:else}
													{formatPercent(eRate * 100)}
												{/if}
											</Table.Cell>
											<Table.Cell class="text-right tabular-nums">{ms(e.p50Ms)}</Table.Cell>
											<Table.Cell class="text-right tabular-nums">{ms(e.p95Ms)}</Table.Cell>
											<Table.Cell class="text-right tabular-nums">{ms(e.p99Ms)}</Table.Cell>
											<Table.Cell class="text-muted-foreground text-right tabular-nums" title={formatDateTime(e.lastSeenUnixMs)}>
												{formatAgo(e.lastSeenUnixMs, nowMs)}
											</Table.Cell>
										</Table.Row>
									{/each}
								</Table.Body>
							</Table.Root>
							{#if detail.endpoints.some((e) => e.endpointSource === 'UrlPath')}
								<p class="text-muted-foreground text-xs">{m.externalApisPage_derivedEndpointHint()}</p>
							{/if}
						{/if}
					</section>

					<section class="space-y-2">
						<h2 class="text-sm font-medium">{m.externalApisPage_topErrorsHeading()}</h2>
						{#if detail.topErrors.length === 0}
							<p class="text-muted-foreground text-sm">{m.externalApisPage_noErrors()}</p>
						{:else}
							<Table.Root>
								<Table.Header>
									<Table.Row>
										<Table.Head>{m.externalApisPage_endpointColumn()}</Table.Head>
										<Table.Head>{m.externalApisPage_statusColumn()}</Table.Head>
										<Table.Head>{m.externalApisPage_errorColumn()}</Table.Head>
										<Table.Head class="text-right">{m.externalApisPage_countColumn()}</Table.Head>
										<Table.Head class="text-right">{m.externalApisPage_lastSeenColumn()}</Table.Head>
									</Table.Row>
								</Table.Header>
								<Table.Body>
									{#each detail.topErrors as err, i (i)}
										<Table.Row>
											<Table.Cell>
												<a
													class="hover:underline"
													href={tracesHref({
														method: err.method,
														endpoint: err.endpoint,
														endpointSource: err.endpointSource,
														statusCode: err.statusCode,
														errorsOnly: true
													})}
												>
													{@render endpointLabel(err.method, err.endpoint)}
												</a>
											</Table.Cell>
											<Table.Cell class="tabular-nums">
												{#if err.statusCode}
													<Badge variant={statusVariant(err.statusCode)}>{err.statusCode}</Badge>
												{:else}
													<span class="text-muted-foreground" title={m.externalApisPage_noResponseTooltip()}>&mdash;</span>
												{/if}
											</Table.Cell>
											<Table.Cell class="max-w-72">
												{#if err.errorType && err.errorType !== err.statusCode}
													<div class="font-mono text-xs break-all">{err.errorType}</div>
												{/if}
												{#if err.sampleMessage}
													<div class="text-muted-foreground line-clamp-2 text-xs" title={err.sampleMessage}>{err.sampleMessage}</div>
												{/if}
											</Table.Cell>
											<Table.Cell class="text-destructive text-right tabular-nums">{formatCount(err.callCount)}</Table.Cell>
											<Table.Cell class="text-muted-foreground text-right tabular-nums" title={formatDateTime(err.lastSeenUnixMs)}>
												{formatAgo(err.lastSeenUnixMs, nowMs)}
											</Table.Cell>
										</Table.Row>
									{/each}
								</Table.Body>
							</Table.Root>
						{/if}
					</section>

					<section class="space-y-2">
						<h2 class="text-sm font-medium">{m.externalApisPage_callersHeading()}</h2>
						<Table.Root>
							<Table.Header>
								<Table.Row>
									<Table.Head>{m.externalApisPage_serviceColumn()}</Table.Head>
									<Table.Head class="text-right">{m.externalApisPage_rateColumn()}</Table.Head>
									<Table.Head class="text-right">{m.externalApisPage_errorRateColumn()}</Table.Head>
									<Table.Head class="text-right">{m.externalApisPage_p50Column()}</Table.Head>
									<Table.Head class="text-right">{m.externalApisPage_p95Column()}</Table.Head>
									<Table.Head class="text-right">{m.externalApisPage_p99Column()}</Table.Head>
								</Table.Row>
							</Table.Header>
							<Table.Body>
								{#each detail.callers as c (c.serviceName)}
									{@const cRate = errorRate(c)}
									<Table.Row>
										<Table.Cell class="font-medium">
											<a
												class="hover:underline"
												href={buildExternalCallTracesHref({ domain: detail.domain, service: c.serviceName }, externalApis.windowPreset)}
											>
												{c.serviceName}
											</a>
										</Table.Cell>
										<Table.Cell class="text-right tabular-nums" title={formatCount(c.callCount)}>{rate(c.perSecond)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums {cRate > 0 ? 'text-destructive' : ''}">{formatPercent(cRate * 100)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{ms(c.p50Ms)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{ms(c.p95Ms)}</Table.Cell>
										<Table.Cell class="text-right tabular-nums">{ms(c.p99Ms)}</Table.Cell>
									</Table.Row>
								{/each}
							</Table.Body>
						</Table.Root>
					</section>
				{/if}
			</div>
		{/if}
	</Sheet.Content>
</Sheet.Root>
