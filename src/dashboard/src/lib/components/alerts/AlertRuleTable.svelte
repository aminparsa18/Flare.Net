<script lang="ts">
	// First real use of the `Table`/`Card` shadcn components in this app (both were
	// scaffolded but unused before this feature - see AlertRuleFormDialog.svelte's own
	// note about `Dialog` being in the same position).
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Input } from '$lib/components/ui/input';
	import { page } from '$app/state';
	import { goto, replaceState } from '$app/navigation';
	import { withBase } from '$lib/paths';
	import { formatBurnRate, formatWindow } from '$lib/slos/burn-rate';
	import { formatDateTime } from '$lib/time/format';
	import {
		applyAlertListView,
		buildAlertListSearch,
		isAlertListViewActive,
		parseAlertListView,
		ruleState,
		RULE_KIND_FILTERS,
		RULE_STATE_FILTERS,
		type AlertListView,
		type RuleSortKey
	} from '$lib/alerts/list-view';
	import MuteRuleMenu from './MuteRuleMenu.svelte';
	import AlertRulesTransfer from './AlertRulesTransfer.svelte';
	import { Spinner } from '$lib/components/ui/spinner';
	import { alertsContext } from '$lib/alerts/context';
	import { maintenanceWindowsContext } from '$lib/maintenance-windows/context';
	import { testAlertRule, sendTestAlertRule, type AlertRule, type AlertTestResult, type AlertNotificationTestResult } from '$lib/alerts-api';
	import { SEVERITY_BUCKETS, severityBucketLabel, severityNumbersForBucket } from '$lib/logs/severity';
	import { severityBadgeVariant, severityLabel } from '$lib/alerts/severity';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import HistoryIcon from '@lucide/svelte/icons/history';
	import PlayIcon from '@lucide/svelte/icons/play';
	import SendIcon from '@lucide/svelte/icons/send';
	import BellIcon from '@lucide/svelte/icons/bell';
	import SearchIcon from '@lucide/svelte/icons/search';
	import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
	import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';

	const alerts = alertsContext.get();
	const maintenance = maintenanceWindowsContext.get();

	// Search/filter/sort live in the URL so a filtered view survives reload and can be shared;
	// hydrated once from the initial URL, then written back with replaceState on every change.
	let view = $state<AlertListView>(parseAlertListView(page.url.searchParams));
	$effect(() => {
		const search = buildAlertListSearch(page.url.searchParams, view);
		if (search !== page.url.search) replaceState(page.url.pathname + search, page.state);
	});

	const visibleRules = $derived(applyAlertListView(alerts.rules, alerts.statuses, (rule) => maintenance.isRuleMuted(rule), view));

	function toggleSort(key: RuleSortKey): void {
		view = view.sort === key ? { ...view, direction: view.direction === 'asc' ? 'desc' : 'asc' } : { ...view, sort: key, direction: key === 'lastFired' ? 'desc' : 'asc' };
	}

	function stateLabel(state: (typeof RULE_STATE_FILTERS)[number]): string {
		return { all: m.alertRuleTable_stateAll(), firing: m.alertRuleTable_stateFiring(), ok: m.alertRuleTable_stateOk(), disabled: m.alertRuleTable_stateDisabled(), muted: m.alertRuleTable_stateMuted() }[state];
	}

	function kindLabel(kind: (typeof RULE_KIND_FILTERS)[number]): string {
		return {
			LogCount: m.alertRuleForm_conditionKindLogCount(),
			MetricThreshold: m.alertRuleForm_conditionKindMetricThreshold(),
			ExceptionCount: m.alertRuleForm_conditionKindExceptionCount(),
			Anomaly: m.alertRuleForm_conditionKindAnomaly(),
			SloBurnRate: m.alertRuleForm_conditionKindSloBurnRate()
		}[kind];
	}

	function summarizeCondition(rule: AlertRule): string {
		if (rule.conditionKind === 'SloBurnRate') return m.alertRuleTable_sloSummary();
		// An Anomaly rule's series is one of the other three kinds' conditions (ADR-0048).
		const kind = rule.conditionKind === 'Anomaly' ? (rule.anomalyCondition?.source ?? 'LogCount') : rule.conditionKind;
		if (kind === 'MetricThreshold') {
			return rule.metricCondition ? `${rule.metricCondition.metricName} (${rule.metricCondition.aggregation})` : m.alertRuleTable_allLogs();
		}

		if (kind === 'ExceptionCount') {
			if (!rule.exceptionCondition) return m.alertRuleTable_allLogs();
			return rule.exceptionCondition.exceptionMessage
				? `${rule.exceptionCondition.exceptionType} (${rule.exceptionCondition.exceptionMessage})`
				: rule.exceptionCondition.exceptionType;
		}

		const parts: string[] = [];
		if (rule.condition.services?.length) parts.push(rule.condition.services.join(', '));
		const severities = rule.condition.severityNumbers ?? [];
		const labels = SEVERITY_BUCKETS.filter((b) => severityNumbersForBucket(b).every((n) => severities.includes(n))).map(
			(b) => severityBucketLabel(b)
		);
		if (labels.length) parts.push(labels.join('/'));
		if (rule.condition.search) parts.push(`"${rule.condition.search}"`);
		return parts.length ? parts.join(' · ') : m.alertRuleTable_allLogs();
	}

	// See AlertsCommand.cs's DescribeChannel (the CLI's own equivalent) - a rule on
	// channelIds shows a count (names would need a channel-id -> name lookup this table
	// doesn't have loaded; the row's "send test"/edit actions are where the actual
	// channels are visible), one still on its legacy inline field shows which one.
	function channelSummary(rule: AlertRule): string {
		if (rule.channelIds.length > 0) {
			return rule.channelIds.length === 1 ? m.alertRuleTable_oneChannel() : m.alertRuleTable_multipleChannels({ count: rule.channelIds.length });
		}

		if (rule.webhookUrl) return m.alertRuleForm_channelWebhook();
		if (rule.telegramBotToken && rule.telegramChatId) return m.alertRuleForm_channelTelegram();
		if (rule.emailTo) return m.alertRuleForm_channelEmail();
		if (rule.pagerDutyRoutingKey) return m.alertRuleForm_channelPagerDuty();
		return '';
	}

	function thresholdText(rule: AlertRule): string {
		if (rule.conditionKind === 'Anomaly' && rule.anomalyCondition) {
			const a = rule.anomalyCondition;
			const z = a.direction === 'Above' ? `z >= ${a.zScoreThreshold}` : a.direction === 'Below' ? `z <= -${a.zScoreThreshold}` : `|z| >= ${a.zScoreThreshold}`;
			return a.seasonality === 'Weekly'
				? m.alertRuleTable_anomalyTextWeekly({ z, periods: a.baselinePeriods, window: rule.windowSeconds })
				: m.alertRuleTable_anomalyTextDaily({ z, periods: a.baselinePeriods, window: rule.windowSeconds });
		}

		if (rule.conditionKind === 'SloBurnRate' && rule.sloCondition) {
			const c = rule.sloCondition;
			return m.alertRuleTable_sloThresholdText({ rate: formatBurnRate(c.burnRateThreshold), long: formatWindow(c.longWindowSeconds), short: formatWindow(c.shortWindowSeconds) });
		}

		const symbol = rule.threshold.comparator === 'LessThan' ? '<' : '>=';
		if (rule.conditionKind === 'MetricThreshold') {
			return m.alertRuleTable_metricThresholdText({ symbol, value: rule.thresholdUnit ? `${rule.metricThresholdValue ?? 0} ${rule.thresholdUnit}` : (rule.metricThresholdValue ?? 0), window: rule.windowSeconds });
		}

		return m.alertRuleTable_thresholdText({ symbol, count: rule.threshold.count, window: rule.windowSeconds });
	}

	async function handleDelete(rule: AlertRule): Promise<void> {
		if (!confirm(m.alertRuleTable_deleteConfirm({ name: rule.name }))) return;
		await alerts.remove(rule.id);
	}

	// Ephemeral, per-row test results - not part of AlertsState since nothing else in
	// the app needs to react to "did I just test this rule", same reasoning
	// VolumeChart.svelte calls aggregateLogs() directly rather than through
	// LogsExplorerState for its own local concern.
	let testResults = $state<Record<string, AlertTestResult | 'loading' | 'error'>>({});

	async function handleTest(rule: AlertRule): Promise<void> {
		testResults = { ...testResults, [rule.id]: 'loading' };
		try {
			const result = await testAlertRule(rule.id);
			testResults = { ...testResults, [rule.id]: result };
		} catch {
			testResults = { ...testResults, [rule.id]: 'error' };
		}
	}

	// "Send test alert" - unlike handleTest above (a dry-run evaluation), this actually
	// notifies through the rule's saved channel, so its config can be verified without
	// waiting for a real breach. Same per-row ephemeral-state shape as testResults.
	let sendTestResults = $state<Record<string, AlertNotificationTestResult | 'loading' | 'error'>>({});

	async function handleSendTest(rule: AlertRule): Promise<void> {
		sendTestResults = { ...sendTestResults, [rule.id]: 'loading' };
		try {
			const result = await sendTestAlertRule(rule.id);
			sendTestResults = { ...sendTestResults, [rule.id]: result };
		} catch {
			sendTestResults = { ...sendTestResults, [rule.id]: 'error' };
		}
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.alertRuleTable_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.alertRuleTable_subheading()}</p>
	</div>
	<div class="flex items-center gap-2">
		<AlertRulesTransfer ruleIds={isAlertListViewActive(view) ? visibleRules.map((r) => r.id) : []} />
		<Button size="sm" onclick={() => alerts.openCreate()}>
			<PlusIcon data-icon="inline-start" />
			{m.alertRuleTable_newAlert()}
		</Button>
	</div>
</div>

{#if alerts.loading}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if alerts.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{alerts.error}</p>
	</div>
{:else if alerts.rules.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<BellIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.alertRuleTable_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.alertRuleTable_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => alerts.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.alertRuleTable_newAlert()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="flex flex-wrap items-center gap-2 border-b px-4 py-2">
		<div class="relative w-64">
			<SearchIcon class="text-muted-foreground pointer-events-none absolute top-1/2 left-2 size-4 -translate-y-1/2" />
			<Input class="h-8 pl-8" placeholder={m.alertRuleTable_searchPlaceholder()} bind:value={view.query} />
		</div>
		<Input class="h-8 w-48" placeholder={m.alertRuleTable_labelFilterPlaceholder()} bind:value={view.label} />
		<select class="border-input bg-background h-8 rounded-md border px-2 text-sm" bind:value={view.state}>
			{#each RULE_STATE_FILTERS as state (state)}
				<option value={state}>{stateLabel(state)}</option>
			{/each}
		</select>
		<select class="border-input bg-background h-8 rounded-md border px-2 text-sm" bind:value={view.kind}>
			<option value="all">{m.alertRuleTable_kindAll()}</option>
			{#each RULE_KIND_FILTERS as kind (kind)}
				<option value={kind}>{kindLabel(kind)}</option>
			{/each}
		</select>
		{#if isAlertListViewActive(view)}
			<Button variant="ghost" size="sm" onclick={() => (view = { ...view, query: '', label: '', state: 'all', kind: 'all' })}>
				{m.alertRuleTable_clearFilters()}
			</Button>
		{/if}
	</div>
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>
						<button type="button" class="inline-flex items-center gap-1" onclick={() => toggleSort('name')}>
							{m.alertRuleTable_colName()}
							{#if view.sort === 'name'}
								{#if view.direction === 'asc'}<ArrowUpIcon class="size-3" />{:else}<ArrowDownIcon class="size-3" />{/if}
							{/if}
						</button>
					</Table.Head>
					<Table.Head>{m.alertRuleTable_colSeverity()}</Table.Head>
					<Table.Head>{m.alertRuleTable_colCondition()}</Table.Head>
					<Table.Head>{m.alertRuleTable_colThreshold()}</Table.Head>
					<Table.Head>{m.alertRuleTable_colCooldown()}</Table.Head>
					<Table.Head>{m.alertRuleTable_colChannel()}</Table.Head>
					<Table.Head>
						<button type="button" class="inline-flex items-center gap-1" onclick={() => toggleSort('state')}>
							{m.alertRuleTable_colStatus()}
							{#if view.sort === 'state'}
								{#if view.direction === 'asc'}<ArrowUpIcon class="size-3" />{:else}<ArrowDownIcon class="size-3" />{/if}
							{/if}
						</button>
					</Table.Head>
					<Table.Head>
						<button type="button" class="inline-flex items-center gap-1" onclick={() => toggleSort('lastFired')}>
							{m.alertRuleTable_colLastFired()}
							{#if view.sort === 'lastFired'}
								{#if view.direction === 'asc'}<ArrowUpIcon class="size-3" />{:else}<ArrowDownIcon class="size-3" />{/if}
							{/if}
						</button>
					</Table.Head>
					<Table.Head class="text-right">{m.alertRuleTable_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each visibleRules as rule (rule.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{rule.name}
							{#if rule.description}
								<p class="text-muted-foreground font-normal">{rule.description}</p>
							{/if}
							{#if Object.keys(rule.labels).length > 0}
								<div class="mt-1 flex flex-wrap gap-1">
									{#each Object.entries(rule.labels) as [key, value] (key)}
										<Badge variant="outline" class="font-mono text-xs font-normal">{key}={value}</Badge>
									{/each}
								</div>
							{/if}
						</Table.Cell>
						<Table.Cell><Badge variant={severityBadgeVariant(rule.severity)}>{severityLabel(rule.severity)}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground">{summarizeCondition(rule)}</Table.Cell>
						<Table.Cell class="font-mono text-xs">
							{thresholdText(rule)}
							{#if rule.noDataWindowSeconds > 0}
								<p class="text-muted-foreground">{m.alertRuleTable_noDataText({ window: rule.noDataWindowSeconds })}</p>
							{/if}
							{#if rule.minDataPoints > 0}
								<p class="text-muted-foreground">{m.alertRuleTable_minDataPointsText({ points: rule.minDataPoints })}</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">
							{rule.cooldownSeconds}s
							{#if rule.evaluationIntervalSeconds > 0}
								<p class="text-xs">
									{m.alertRuleTable_evaluateEveryText({
										interval:
											rule.evaluationIntervalSeconds % 60 === 0
												? m.alertRuleForm_evaluateEveryMinutes({ minutes: rule.evaluationIntervalSeconds / 60 })
												: m.alertRuleForm_evaluateEverySeconds({ seconds: rule.evaluationIntervalSeconds })
									})}
								</p>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground">{channelSummary(rule)}</Table.Cell>
						<Table.Cell>
							{#if ruleState(rule, alerts.statuses.get(rule.id)) === 'firing'}
								<Badge variant="destructive" class="mr-1">{m.alertRuleTable_stateFiring()}</Badge>
							{/if}
							<Badge variant={rule.enabled ? 'secondary' : 'outline'}
								>{rule.enabled ? m.alertRuleTable_enabled() : m.alertRuleTable_disabled()}</Badge
							>
							{#if rule.enabled && maintenance.isRuleMuted(rule)}
								<Badge variant="outline" class="ml-1" title={m.alertRuleTable_mutedHint()}
									>{maintenance.muteEndsAt(rule) ? m.alertRuleTable_mutedUntil({ time: formatDateTime(maintenance.muteEndsAt(rule)!) }) : m.alertRuleTable_muted()}</Badge
								>
							{/if}
							{#if testResults[rule.id] === 'loading'}
								<Badge variant="outline" class="ml-1">{m.alertRuleTable_testing()}</Badge>
							{:else if testResults[rule.id] === 'error'}
								<Badge variant="destructive" class="ml-1">{m.alertRuleTable_testFailed()}</Badge>
							{:else if testResults[rule.id]}
								{@const result = testResults[rule.id] as AlertTestResult}
								<Badge variant={result.wouldFire ? 'warning' : 'outline'} class="ml-1">
									{#if result.noData}
										{m.alertRuleTable_testResultNoData()}
									{:else if result.insufficientData}
										{m.alertRuleTable_testResultInsufficientData({ points: result.dataPointCount ?? 0 })}
									{:else if result.conditionKind === 'Anomaly'}
										{result.zScore === undefined
											? m.alertRuleTable_testResultAnomalyNoHistory()
											: result.wouldFire
												? m.alertRuleTable_testResultAnomalyFiring({ z: result.zScore.toFixed(1) })
												: m.alertRuleTable_testResultAnomalyNotFiring({ z: result.zScore.toFixed(1) })}
									{:else if result.conditionKind === 'SloBurnRate'}
										{result.wouldFire
											? m.alertRuleTable_testResultFiringSlo({ rate: formatBurnRate(result.observedValue ?? 0) })
											: m.alertRuleTable_testResultNotFiringSlo({ rate: formatBurnRate(result.observedValue ?? 0) })}
									{:else if result.conditionKind === 'MetricThreshold'}
										{result.wouldFire
											? m.alertRuleTable_testResultFiringMetric({ value: result.observedValue ?? 0 })
											: m.alertRuleTable_testResultNotFiringMetric({ value: result.observedValue ?? 0 })}
									{:else}
										{result.wouldFire
											? m.alertRuleTable_testResultFiring({ count: result.observedCount })
											: m.alertRuleTable_testResultNotFiring({ count: result.observedCount })}
									{/if}
								</Badge>
							{/if}
							{#if sendTestResults[rule.id] === 'loading'}
								<Badge variant="outline" class="ml-1">{m.alertRuleTable_testing()}</Badge>
							{:else if sendTestResults[rule.id] === 'error'}
								<Badge variant="destructive" class="ml-1">{m.alertRuleTable_sendTestFailed()}</Badge>
							{:else if sendTestResults[rule.id]}
								{@const sendResult = sendTestResults[rule.id] as AlertNotificationTestResult}
								<Badge variant={sendResult.success ? 'secondary' : 'destructive'} class="ml-1">
									{sendResult.success ? m.alertRuleTable_sendTestSent() : m.alertRuleTable_sendTestFailed()}
								</Badge>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-muted-foreground text-xs whitespace-nowrap">
							{alerts.statuses.get(rule.id)?.lastFiredAt ? formatDateTime(alerts.statuses.get(rule.id)!.lastFiredAt!) : m.alertRuleTable_neverFired()}
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button
								variant="ghost"
								size="icon-sm"
								title={m.alertRuleTable_actionTest()}
								onclick={() => handleTest(rule)}
							>
								<PlayIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								title={m.alertRuleTable_actionSendTest()}
								onclick={() => handleSendTest(rule)}
							>
								<SendIcon />
							</Button>
							<MuteRuleMenu {rule} />
							<Button variant="ghost" size="icon-sm" title={m.alertRuleTable_actionHistory()} onclick={() => alerts.openHistory(rule)}>
								<HistoryIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								title={m.alertRuleTable_actionEdit()}
								onclick={() => (rule.conditionKind === 'SloBurnRate' && rule.sloCondition ? goto(withBase(`/settings/slos?slo=${rule.sloCondition.sloId}`)) : alerts.openEdit(rule))}
							>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.alertRuleTable_actionDelete()}
								onclick={() => handleDelete(rule)}
							>
								<Trash2Icon />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
		{#if visibleRules.length === 0}
			<p class="text-muted-foreground p-6 text-center text-sm">{m.alertRuleTable_noMatches()}</p>
		{/if}
	</div>
{/if}
