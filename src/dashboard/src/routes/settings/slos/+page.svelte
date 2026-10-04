<script lang="ts">
	// Service level objectives over entry-span data, with error budgets and burn-rate alerting -
	// see docs-internal/adr/0108-slo-error-budgets.md.
	import { onMount } from 'svelte';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import SloFormDialog from '$lib/components/slos/SloFormDialog.svelte';
	import SloDetailSheet from '$lib/components/slos/SloDetailSheet.svelte';
	import { authContext } from '$lib/auth/context';
	import { deleteSlo, getSloStatus, listSlos, type Slo, type SloStatus } from '$lib/slos-api';
	import { getServiceOverview } from '$lib/services-api';
	import { listAlertRules } from '$lib/alerts-api';
	import { formatBudget } from '$lib/slos/budget';
	import { formatBurnRate } from '$lib/slos/burn-rate';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import TargetIcon from '@lucide/svelte/icons/target';

	const auth = authContext.get();

	let slos = $state<Slo[]>([]);
	let statuses = $state<Record<string, SloStatus | 'error'>>({});
	/** SLO id -> number of burn-rate alert rules watching it. */
	let alertCounts = $state<Record<string, number>>({});
	let serviceNames = $state<string[]>([]);
	let loading = $state(true);
	let error = $state<string | null>(null);
	let selected = $state<Slo | null>(null);
	let formTarget = $state<Slo | 'new' | null>(null);

	async function load(): Promise<void> {
		try {
			slos = await listSlos();
			error = null;
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
			loading = false;
			return;
		}
		loading = false;
		// Statuses and alert counts fill in after the list renders; a failure on either leaves its cell blank.
		await Promise.all([loadStatuses(), loadAlertCounts()]);
	}

	async function loadStatuses(): Promise<void> {
		await Promise.all(
			slos.map(async (slo) => {
				// Awaited before the spread: the loads run concurrently, and spreading `statuses` first
				// would write back a stale copy that drops a sibling's result.
				let result: SloStatus | 'error';
				try {
					result = await getSloStatus(slo.id);
				} catch {
					result = 'error';
				}
				statuses = { ...statuses, [slo.id]: result };
			})
		);
	}

	async function loadAlertCounts(): Promise<void> {
		try {
			const counts: Record<string, number> = {};
			for (const rule of (await listAlertRules()).rules) {
				if (rule.conditionKind === 'SloBurnRate' && rule.sloCondition) {
					counts[rule.sloCondition.sloId] = (counts[rule.sloCondition.sloId] ?? 0) + 1;
				}
			}
			alertCounts = counts;
		} catch {
			// The alert badges are a nicety; the list stands without them.
		}
	}

	onMount(async () => {
		getServiceOverview(1440)
			.then((r) => (serviceNames = r.services.map((s) => s.serviceName).sort()))
			.catch(() => {});
		await load();
		const id = page.url.searchParams.get('slo');
		if (id) selected = slos.find((s) => s.id === id) ?? null;
	});

	function open(slo: Slo | null): void {
		selected = slo;
		// Keep the URL shareable (the burn-rate alert notifications link here).
		const url = new URL(page.url);
		if (slo) url.searchParams.set('slo', slo.id);
		else url.searchParams.delete('slo');
		goto(url, { replaceState: true, noScroll: true, keepFocus: true });
	}

	async function onSaved(slo: Slo): Promise<void> {
		formTarget = null;
		await load();
		if (selected?.id === slo.id) selected = slos.find((s) => s.id === slo.id) ?? null;
	}

	async function onDelete(slo: Slo): Promise<void> {
		if (!confirm(m.sloPage_deleteConfirm({ name: slo.name }))) return;
		try {
			await deleteSlo(slo.id);
			open(null);
			await load();
		} catch (e) {
			error = e instanceof Error ? e.message : String(e);
		}
	}

	const objective = (slo: Slo) =>
		slo.kind === 'Latency'
			? m.sloTable_objectiveLatency({ target: slo.targetPercent, threshold: slo.latencyThresholdMs, days: slo.windowDays })
			: m.sloTable_objectiveAvailability({ target: slo.targetPercent, days: slo.windowDays });

	const burn1h = (status: SloStatus | 'error' | undefined) => (status && status !== 'error' ? status.burnRates.find((w) => w.windowSeconds === 3600)?.burnRate : null);
</script>

<svelte:head>
	<title>{m.sloPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<div class="flex items-center justify-between border-b px-4 py-3">
		<div>
			<h1 class="text-sm font-semibold">{m.sloPage_heading()}</h1>
			<p class="text-muted-foreground text-xs">{m.sloPage_description()}</p>
		</div>
		{#if auth.canMutate}
			<Button size="sm" onclick={() => (formTarget = 'new')}>
				<PlusIcon data-icon="inline-start" />
				{m.sloPage_new()}
			</Button>
		{/if}
	</div>

	{#if loading}
		<div class="flex flex-1 items-center justify-center"><Spinner /></div>
	{:else if error}
		<div class="flex flex-1 items-center justify-center"><p class="text-destructive text-sm">{error}</p></div>
	{:else if slos.length === 0}
		<Empty.Root class="flex-1">
			<Empty.Header>
				<Empty.Media><TargetIcon class="text-muted-foreground size-8" /></Empty.Media>
				<Empty.Title>{m.sloPage_emptyTitle()}</Empty.Title>
				<Empty.Description>{m.sloPage_emptyDescription()}</Empty.Description>
			</Empty.Header>
			{#if auth.canMutate}
				<Empty.Content>
					<Button size="sm" onclick={() => (formTarget = 'new')}>
						<PlusIcon data-icon="inline-start" />
						{m.sloPage_new()}
					</Button>
				</Empty.Content>
			{/if}
		</Empty.Root>
	{:else}
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.sloTable_colName()}</Table.Head>
					<Table.Head>{m.sloTable_colScope()}</Table.Head>
					<Table.Head>{m.sloTable_colObjective()}</Table.Head>
					<Table.Head class="text-right">{m.sloTable_colSli()}</Table.Head>
					<Table.Head class="w-44">{m.sloTable_colBudget()}</Table.Head>
					<Table.Head class="text-right">{m.sloTable_colBurn()}</Table.Head>
					<Table.Head>{m.sloTable_colAlerts()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each slos as slo (slo.id)}
					{@const status = statuses[slo.id]}
					{@const budget = status && status !== 'error' ? status.errorBudgetRemaining : null}
					{@const rate = burn1h(status)}
					<Table.Row class="cursor-pointer" onclick={() => open(slo)}>
						<Table.Cell class="font-medium">{slo.name}</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">{slo.serviceName}{slo.operationName ? ` · ${slo.operationName}` : ''}</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">{objective(slo)}</Table.Cell>
						<Table.Cell class="text-right tabular-nums">
							{status && status !== 'error' && status.sli != null ? `${status.sli.toFixed(2)}%` : '-'}
						</Table.Cell>
						<Table.Cell>
							{#if budget != null}
								<div class="flex items-center gap-2">
									<div class="bg-muted h-1.5 w-24 overflow-hidden rounded-full">
										<div class="h-full {budget < 0.1 ? 'bg-destructive' : budget < 0.5 ? 'bg-warning' : 'bg-primary'}" style="width: {Math.max(0, Math.min(1, budget)) * 100}%"></div>
									</div>
									<span class="text-xs tabular-nums {budget < 0 ? 'text-destructive' : ''}">{formatBudget(budget)}</span>
								</div>
							{:else}
								<span class="text-muted-foreground text-xs">-</span>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right">
							{#if rate != null}
								<Badge variant={rate >= 14 ? 'destructive' : rate >= 1 ? 'warning' : 'secondary'}>{formatBurnRate(rate)}x</Badge>
							{:else}
								<span class="text-muted-foreground text-xs">-</span>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs">{alertCounts[slo.id] ? m.sloTable_alertCount({ count: alertCounts[slo.id] }) : m.sloTable_noAlerts()}</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	{/if}
</div>

<SloDetailSheet
	slo={selected}
	onclose={() => open(null)}
	onedit={(slo) => (formTarget = slo)}
	ondelete={onDelete}
	onalertschanged={loadAlertCounts}
/>
<SloFormDialog target={formTarget} {serviceNames} onclose={() => (formTarget = null)} onsaved={onSaved} />
