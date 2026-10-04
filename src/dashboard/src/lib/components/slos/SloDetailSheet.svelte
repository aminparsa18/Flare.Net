<script lang="ts">
	// One SLO's drill-down: SLI vs target, the error budget burn-down over its window, the burn
	// rate over the standard alerting windows, and the burn-rate alert rules that watch it (with a
	// one-click way to create the standard fast/slow pair). See docs-internal/adr/0108-slo-error-budgets.md.
	import * as Sheet from '$lib/components/ui/sheet';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import BudgetChart from './BudgetChart.svelte';
	import { authContext } from '$lib/auth/context';
	import { getSloStatus, type Slo, type SloStatus } from '$lib/slos-api';
	import { createAlertRule, deleteAlertRule, listAlertRules, listAlertRuleStatuses, type AlertRule, type AlertRuleStatus } from '$lib/alerts-api';
	import { listNotificationChannels, type NotificationChannel } from '$lib/notification-channels-api';
	import { BURN_ALERT_PRESETS, burnRateThreshold, formatBurnRate, formatWindow, type BurnAlertPreset } from '$lib/slos/burn-rate';
	import { budgetSeries, formatBudget } from '$lib/slos/budget';
	import { formatCount } from '$lib/ingestion/format';
	import { withBase } from '$lib/paths';
	import * as m from '$lib/paraglide/messages';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';

	let {
		slo,
		onclose,
		onedit,
		ondelete,
		onalertschanged
	}: {
		slo: Slo | null;
		onclose: () => void;
		onedit: (slo: Slo) => void;
		ondelete: (slo: Slo) => void;
		/** Called after burn-rate rules were created or deleted, so the list can refresh its alert badges. */
		onalertschanged: () => void;
	} = $props();

	const auth = authContext.get();

	let status = $state<SloStatus | null>(null);
	let rules = $state<AlertRule[]>([]);
	let ruleStatuses = $state<AlertRuleStatus[]>([]);
	let channels = $state<NotificationChannel[]>([]);
	let selectedChannels = $state<string[]>([]);
	let loading = $state(false);
	let error = $state<string | null>(null);
	let creating = $state(false);
	let alertError = $state<string | null>(null);

	// One load per opened SLO; a stale response for a SLO since closed/switched is dropped.
	$effect(() => {
		const current = slo;
		status = null;
		alertError = null;
		if (!current) return;
		const controller = new AbortController();
		loading = true;
		error = null;
		Promise.all([getSloStatus(current.id, controller.signal), loadAlerts(controller.signal)])
			.then(([s]) => (status = s))
			.catch((e) => {
				if (!controller.signal.aborted) error = e instanceof Error ? e.message : String(e);
			})
			.finally(() => {
				if (!controller.signal.aborted) loading = false;
			});
		return () => controller.abort();
	});

	async function loadAlerts(signal?: AbortSignal): Promise<void> {
		const [all, states, chans] = await Promise.all([
			listAlertRules(signal),
			listAlertRuleStatuses(signal).catch(() => [] as AlertRuleStatus[]),
			listNotificationChannels(signal).catch(() => ({ channels: [] as NotificationChannel[] }))
		]);
		rules = all.rules;
		ruleStatuses = states;
		channels = chans.channels;
	}

	const burnRules = $derived(rules.filter((r) => r.conditionKind === 'SloBurnRate' && r.sloCondition?.sloId === slo?.id));
	const firing = (rule: AlertRule) => ruleStatuses.find((s) => s.ruleId === rule.id)?.firing ?? false;
	const budget = $derived(status ? budgetSeries(status.series, status.slo.targetPercent, status.slo.windowDays) : []);
	const channelOptions = $derived(channels.map((c) => ({ value: c.id, label: c.name })));

	function presetLabel(preset: BurnAlertPreset): string {
		return preset.key === 'fast' ? m.sloDetail_fastBurn() : m.sloDetail_slowBurn();
	}

	async function createPair(): Promise<void> {
		if (!slo || creating) return;
		creating = true;
		alertError = null;
		try {
			for (const preset of BURN_ALERT_PRESETS) {
				// Skip a preset that already has a rule (same windows), so a second click only fills the gap.
				if (burnRules.some((r) => r.sloCondition?.longWindowSeconds === preset.longWindowSeconds)) continue;
				await createAlertRule({
					name: `${slo.name} - ${presetLabel(preset)} (${formatWindow(preset.longWindowSeconds)}/${formatWindow(preset.shortWindowSeconds)})`,
					condition: {},
					threshold: { count: 1, comparator: 'GreaterThanOrEqual' },
					windowSeconds: preset.longWindowSeconds,
					conditionKind: 'SloBurnRate',
					sloCondition: {
						sloId: slo.id,
						longWindowSeconds: preset.longWindowSeconds,
						shortWindowSeconds: preset.shortWindowSeconds,
						burnRateThreshold: burnRateThreshold(preset, slo.windowDays)
					},
					severity: preset.severity,
					channelIds: selectedChannels
				});
			}
			await loadAlerts();
			onalertschanged();
		} catch (e) {
			alertError = e instanceof Error ? e.message : String(e);
		} finally {
			creating = false;
		}
	}

	async function removeRule(rule: AlertRule): Promise<void> {
		if (!confirm(m.sloDetail_deleteRuleConfirm({ name: rule.name }))) return;
		try {
			await deleteAlertRule(rule.id);
			await loadAlerts();
			onalertschanged();
		} catch (e) {
			alertError = e instanceof Error ? e.message : String(e);
		}
	}

	const missingPresets = $derived(BURN_ALERT_PRESETS.filter((p) => !burnRules.some((r) => r.sloCondition?.longWindowSeconds === p.longWindowSeconds)));
	const burnVariant = (rate: number | null) => (rate == null ? 'outline' : rate >= 14 ? 'destructive' : rate >= 1 ? 'warning' : 'secondary');
</script>

<Sheet.Root open={slo !== null} onOpenChange={(next) => !next && onclose()}>
	<Sheet.Content class="flex w-full flex-col overflow-y-auto data-[side=right]:sm:max-w-3xl">
		{#if slo}
			<Sheet.Header>
				<Sheet.Title class="flex flex-wrap items-center gap-2">
					{slo.name}
					<Badge variant="secondary">{slo.kind === 'Latency' ? m.sloKind_latency() : m.sloKind_availability()}</Badge>
				</Sheet.Title>
				<Sheet.Description>
					{slo.serviceName}{slo.operationName ? ` · ${slo.operationName}` : ''} ·
					{slo.kind === 'Latency'
						? m.sloTable_objectiveLatency({ target: slo.targetPercent, threshold: slo.latencyThresholdMs, days: slo.windowDays })
						: m.sloTable_objectiveAvailability({ target: slo.targetPercent, days: slo.windowDays })}
				</Sheet.Description>
				{#if slo.description}
					<p class="text-muted-foreground text-xs">{slo.description}</p>
				{/if}
			</Sheet.Header>

			<div class="flex flex-col gap-5 px-4 pb-6">
				{#if auth.canMutate}
					<div class="flex gap-2">
						<Button variant="outline" size="sm" onclick={() => onedit(slo)}><PencilIcon data-icon="inline-start" />{m.sloDetail_edit()}</Button>
						<Button variant="outline" size="sm" onclick={() => ondelete(slo)}><Trash2Icon data-icon="inline-start" />{m.sloDetail_delete()}</Button>
					</div>
				{/if}

				{#if loading && !status}
					<div class="flex justify-center py-8"><Spinner /></div>
				{:else if error}
					<p class="text-destructive text-sm">{error}</p>
				{:else if status}
					<div class="grid grid-cols-2 gap-3 sm:grid-cols-4">
						<div>
							<p class="text-muted-foreground text-xs">{m.sloDetail_sli()}</p>
							<p class="text-lg font-semibold">{status.sli == null ? '-' : `${status.sli.toFixed(3)}%`}</p>
						</div>
						<div>
							<p class="text-muted-foreground text-xs">{m.sloDetail_target()}</p>
							<p class="text-lg font-semibold">{slo.targetPercent}%</p>
						</div>
						<div>
							<p class="text-muted-foreground text-xs">{m.sloDetail_budgetRemaining()}</p>
							<p class="text-lg font-semibold {status.errorBudgetRemaining != null && status.errorBudgetRemaining < 0 ? 'text-destructive' : ''}">{formatBudget(status.errorBudgetRemaining)}</p>
						</div>
						<div>
							<p class="text-muted-foreground text-xs">{m.sloDetail_events()}</p>
							<p class="text-lg font-semibold">{formatCount(status.bad)} / {formatCount(status.total)}</p>
							<p class="text-muted-foreground text-[10px]">{m.sloDetail_badOfTotal()}</p>
						</div>
					</div>
					{#if status.total === 0}
						<p class="text-muted-foreground text-xs">{m.sloDetail_noData()}</p>
					{/if}

					<section>
						<h3 class="mb-1 text-sm font-semibold">{m.sloDetail_burnDown()}</h3>
						<BudgetChart points={budget} />
					</section>

					<section>
						<h3 class="mb-1 text-sm font-semibold">{m.sloDetail_burnRates()}</h3>
						<p class="text-muted-foreground mb-2 text-xs">{m.sloDetail_burnRatesHint()}</p>
						<div class="flex flex-wrap gap-2">
							{#each status.burnRates as w (w.windowSeconds)}
								<div class="rounded-md border px-3 py-2 text-center">
									<p class="text-muted-foreground text-[10px]">{formatWindow(w.windowSeconds)}</p>
									<Badge variant={burnVariant(w.burnRate)}>{w.burnRate == null ? '-' : `${formatBurnRate(w.burnRate)}x`}</Badge>
								</div>
							{/each}
						</div>
					</section>
				{/if}

				<section>
					<h3 class="mb-1 text-sm font-semibold">{m.sloDetail_alerts()}</h3>
					<p class="text-muted-foreground mb-2 text-xs">{m.sloDetail_alertsHint()}</p>
					{#if burnRules.length > 0}
						<Table.Root>
							<Table.Body>
								{#each burnRules as rule (rule.id)}
									<Table.Row>
										<Table.Cell class="font-medium">
											<a class="hover:underline" href={withBase(`/alerts?rule=${rule.id}`)}>{rule.name}</a>
										</Table.Cell>
										<Table.Cell class="text-muted-foreground text-xs">
											{m.sloDetail_ruleCondition({
												rate: formatBurnRate(rule.sloCondition?.burnRateThreshold ?? 0),
												long: formatWindow(rule.sloCondition?.longWindowSeconds ?? 0),
												short: formatWindow(rule.sloCondition?.shortWindowSeconds ?? 0)
											})}
										</Table.Cell>
										<Table.Cell>
											<Badge variant={firing(rule) ? 'destructive' : 'secondary'}>{firing(rule) ? m.sloDetail_firing() : m.sloDetail_ok()}</Badge>
										</Table.Cell>
										{#if auth.canMutate}
											<Table.Cell class="text-right">
												<Button variant="ghost" size="icon-sm" title={m.sloDetail_deleteRule()} onclick={() => removeRule(rule)}><Trash2Icon /></Button>
											</Table.Cell>
										{/if}
									</Table.Row>
								{/each}
							</Table.Body>
						</Table.Root>
					{/if}

					{#if auth.canMutate && missingPresets.length > 0}
						<div class="mt-2 flex flex-col gap-2 rounded-md border p-3">
							<ul class="text-muted-foreground list-disc pl-4 text-xs">
								{#each missingPresets as preset (preset.key)}
									<li>
										{m.sloDetail_presetDescription({
											name: presetLabel(preset),
											rate: formatBurnRate(burnRateThreshold(preset, slo.windowDays)),
											long: formatWindow(preset.longWindowSeconds),
											short: formatWindow(preset.shortWindowSeconds),
											percent: Math.round(preset.budgetFraction * 100)
										})}
									</li>
								{/each}
							</ul>
							<div class="flex flex-wrap items-center gap-2">
								<PopoverMultiSelect
									label={m.sloDetail_channelsLabel()}
									options={channelOptions}
									selected={selectedChannels}
									onChange={(next) => (selectedChannels = next)}
								/>
								<Button size="sm" onclick={createPair} disabled={creating || selectedChannels.length === 0}>
									{#if creating}<Spinner class="size-3.5" />{/if}
									{burnRules.length === 0 ? m.sloDetail_createAlerts() : m.sloDetail_createMissingAlerts()}
								</Button>
							</div>
							{#if channels.length === 0}
								<p class="text-muted-foreground text-xs">{m.sloDetail_noChannels()}</p>
							{/if}
						</div>
					{/if}
					{#if alertError}
						<p class="text-destructive mt-2 text-xs">{alertError}</p>
					{/if}
				</section>
			</div>
		{/if}
	</Sheet.Content>
</Sheet.Root>
