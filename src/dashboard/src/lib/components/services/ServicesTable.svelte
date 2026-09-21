<script lang="ts">
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Spinner } from '$lib/components/ui/spinner';
	import ChevronUpIcon from '@lucide/svelte/icons/chevron-up';
	import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
	import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
	import ActivityIcon from '@lucide/svelte/icons/activity';
	import { servicesContext } from '$lib/services/context';
	import { authContext } from '$lib/auth/context';
	import type { ServicesSortColumn } from '$lib/services/state.svelte';
	import { formatMs, formatPercent } from '$lib/indexing/format';
	import { formatRequestRate } from '$lib/services/format';
	import { buildTracesDeepLinkHref } from '$lib/deep-links';
	import ApdexThresholdPopover from './ApdexThresholdPopover.svelte';
	import * as m from '$lib/paraglide/messages';

	const services = servicesContext.get();
	const auth = authContext.get();

	// Same "Admin, or auth off entirely" gating as nav-links.ts's Admin-only /auth link -
	// mutating a service's Apdex threshold is a global, cross-user setting
	// (ApdexThresholdEndpoints is Admin-only server-side); this just keeps a
	// Viewer/Member from ever seeing a control that would 403.
	const canEditApdexThresholds = $derived(!auth.authEnabled || auth.currentUser?.role === 'Admin');

	// Standard Apdex satisfaction bands (Excellent/Good/Fair/Poor/Unacceptable) - same
	// tiered-severity-class convention as errorRateClass below, just five tiers instead
	// of two since Apdex itself is already a 0-1 scale, not a raw rate.
	function apdexRatingClass(score: number): string {
		// text-emerald-* for "healthy" is IngestionHealthStatus's own convention, reused
		// here rather than a `text-success` token this theme doesn't define.
		if (score >= 0.94) return 'text-emerald-600 dark:text-emerald-400 font-medium';
		if (score >= 0.85) return '';
		if (score >= 0.7) return 'text-warning';
		return 'text-destructive font-medium';
	}

	function apdexRatingLabel(score: number): string {
		if (score >= 0.94) return m.servicesTable_apdexExcellent();
		if (score >= 0.85) return m.servicesTable_apdexGood();
		if (score >= 0.7) return m.servicesTable_apdexFair();
		if (score >= 0.5) return m.servicesTable_apdexPoor();
		return m.servicesTable_apdexUnacceptable();
	}

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
		{ column: 'p99DurationMs', label: m.servicesTable_p99Column(), align: 'right' },
		{ column: 'apdexScore', label: m.servicesTable_apdexColumn(), align: 'right' }
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
						<Table.Cell class="text-right tabular-nums">
							<div class="flex items-center justify-end gap-1">
								{#if service.apdexScore == null}
									<span class="text-muted-foreground">&mdash;</span>
								{:else}
									<span class={apdexRatingClass(service.apdexScore)} title={apdexRatingLabel(service.apdexScore)}>
										{service.apdexScore.toFixed(2)}
									</span>
								{/if}
								{#if canEditApdexThresholds}
									<ApdexThresholdPopover
										serviceName={service.serviceName}
										currentThresholdMs={service.apdexThresholdMs}
										defaultThresholdMs={services.apdexThresholds?.defaultThresholdMs ?? service.apdexThresholdMs}
										hasOverride={services.apdexThresholds?.overrides[service.serviceName] != null}
										onSave={(thresholdMs) => services.setApdexThreshold(service.serviceName, thresholdMs)}
										onReset={() => services.resetApdexThreshold(service.serviceName)}
									/>
								{/if}
							</div>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>
