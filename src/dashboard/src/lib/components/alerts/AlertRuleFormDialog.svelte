<script lang="ts">
	// Create/edit alert rule. Uses `Dialog` (scaffolded in the ui/ tree but unused
	// anywhere before this feature) rather than `Sheet` - a bounded form fits Dialog's
	// modal-and-done shape better than Sheet's established "detail viewer" role
	// (EventDetailSheet.svelte). AlertHistorySheet.svelte uses Sheet instead, matching
	// that precedent for its own "inspect a list of things" role.
	import { onMount } from 'svelte';
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Switch } from '$lib/components/ui/switch';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import PopoverMultiSelect from '$lib/components/logs/PopoverMultiSelect.svelte';
	import { alertsContext } from '$lib/alerts/context';
	import { notificationChannelsContext } from '$lib/notification-channels/context';
	import {
		testDraftAlertRule,
		sendTestDraftAlertRule,
		type AlertRuleRequest,
		type ThresholdComparator,
		type AlertTestResult,
		type AlertNotificationTestResult,
		type AlertConditionKind,
		type MetricAlertAggregation
	} from '$lib/alerts-api';
	import { aggregateLogs } from '$lib/api';
	import { getMetricNames, type MetricNameInfo, type MetricPointType } from '$lib/metrics-api';
	import { SEVERITY_BUCKETS, severityBucketLabel, severityNumbersForBucket } from '$lib/logs/severity';
	import * as m from '$lib/paraglide/messages';

	// Which MetricAlertAggregation values are meaningful for each MetricPointType - see
	// that enum's C#-side doc comment. Drives the aggregation Select's option list, the
	// same "restrict the choice, don't validate it server-side" convention
	// MetricQueryRequest.Type's own doc comment documents.
	const AGGREGATIONS_BY_TYPE: Record<MetricPointType, MetricAlertAggregation[]> = {
		Gauge: ['Value'],
		Sum: ['Value', 'Count'],
		Histogram: ['Count', 'Sum', 'P50', 'P75', 'P90', 'P95', 'P99', 'MaxApprox']
	};

	const alerts = alertsContext.get();
	// Shared with the page's own Channels tab (routes/alerts/+page.svelte sets both
	// contexts) - reusing that already-loaded state instead of a separate fetch here
	// means a channel created/edited on that tab is immediately visible in this picker.
	const channels = notificationChannelsContext.get();

	const open = $derived(alerts.formTarget !== null);
	const isEdit = $derived(alerts.formTarget !== null && alerts.formTarget !== 'new');

	let name = $state('');
	let description = $state('');
	let enabled = $state(true);
	let services = $state<string[]>([]);
	let severityNumbers = $state<number[]>([]);
	let search = $state('');
	let thresholdCountText = $state('1');
	let comparator = $state<ThresholdComparator>('GreaterThanOrEqual');
	let windowSecondsText = $state('300');
	let cooldownSecondsText = $state('300');
	let channel = $state<'webhook' | 'telegram' | 'email' | 'pagerduty'>('webhook');
	let webhookUrl = $state('');
	let telegramBotToken = $state('');
	let telegramChatId = $state('');
	let emailTo = $state('');
	let pagerDutyRoutingKey = $state('');

	// Reusable notification channels (see
	// docs-internal/adr/0021-reusable-notification-channels.md) - the channel-picker
	// counterpart to `channel`/`webhookUrl`/etc. above. `usingLegacyChannel` is decided
	// once per dialog-open (in the reset $effect below), not re-derived per keystroke: a
	// rule already using its legacy inline channel keeps showing that same block when
	// edited (no forced migration), while a new rule or one already on `channelIds` gets
	// the multi-select instead - see that $effect for the exact rule.
	let usingLegacyChannel = $state(false);
	let selectedChannelIds = $state<string[]>([]);

	// Metric-threshold condition (AlertConditionKind.MetricThreshold) - see
	// docs-internal/adr/0020-metric-threshold-alerting.md. conditionKind toggles which of
	// this block or the services/severity/search block above buildRequest() reads from.
	let conditionKind = $state<AlertConditionKind>('LogCount');
	let metricName = $state('');
	let metricType = $state<MetricPointType>('Gauge');
	let metricAggregation = $state<MetricAlertAggregation>('Value');
	let metricThresholdValueText = $state('0');

	let testResult = $state<AlertTestResult | null>(null);
	let testing = $state(false);
	let testError = $state<string | null>(null);

	let sendTestResult = $state<AlertNotificationTestResult | null>(null);
	let sendingTest = $state(false);
	let sendTestError = $state<string | null>(null);

	// Resets the draft whenever the dialog opens for a different target - `formTarget`
	// only ever transitions null <-> 'new'|AlertRule at open/close time, so this doesn't
	// fight the user's in-progress edits on every keystroke.
	$effect(() => {
		const target = alerts.formTarget;
		testResult = null;
		testError = null;
		sendTestResult = null;
		sendTestError = null;
		if (target === 'new') {
			name = '';
			description = '';
			enabled = true;
			services = [];
			severityNumbers = [];
			search = '';
			thresholdCountText = '1';
			comparator = 'GreaterThanOrEqual';
			windowSecondsText = '300';
			cooldownSecondsText = '300';
			channel = 'webhook';
			webhookUrl = '';
			telegramBotToken = '';
			telegramChatId = '';
			emailTo = '';
			pagerDutyRoutingKey = '';
			// A new rule only ever gets the channel picker - there's no legacy value to
			// preserve.
			usingLegacyChannel = false;
			selectedChannelIds = [];
			conditionKind = 'LogCount';
			metricName = '';
			metricType = 'Gauge';
			metricAggregation = 'Value';
			metricThresholdValueText = '0';
		} else if (target) {
			name = target.name;
			description = target.description;
			enabled = target.enabled;
			services = target.condition.services ?? [];
			severityNumbers = target.condition.severityNumbers ?? [];
			search = target.condition.search ?? '';
			thresholdCountText = String(target.threshold.count);
			comparator = target.threshold.comparator;
			windowSecondsText = String(target.windowSeconds);
			cooldownSecondsText = String(target.cooldownSeconds);
			channel = target.telegramBotToken || target.telegramChatId
				? 'telegram'
				: target.emailTo
					? 'email'
					: target.pagerDutyRoutingKey
						? 'pagerduty'
						: 'webhook';
			webhookUrl = target.webhookUrl;
			telegramBotToken = target.telegramBotToken;
			telegramChatId = target.telegramChatId;
			emailTo = target.emailTo;
			pagerDutyRoutingKey = target.pagerDutyRoutingKey;
			// A rule already on channelIds keeps using the picker; one still on its legacy
			// inline channel (channelIds empty) keeps showing that same block, unchanged -
			// no forced migration on edit.
			usingLegacyChannel = target.channelIds.length === 0;
			selectedChannelIds = [...target.channelIds];
			conditionKind = target.conditionKind;
			metricName = target.metricCondition?.metricName ?? '';
			metricType = target.metricCondition?.type ?? 'Gauge';
			metricAggregation = target.metricCondition?.aggregation ?? 'Value';
			metricThresholdValueText = String(target.metricThresholdValue ?? 0);
		}
	});

	const thresholdCount = $derived(Number(thresholdCountText));
	const windowSeconds = $derived(Number(windowSecondsText));
	const cooldownSeconds = $derived(Number(cooldownSecondsText));
	const metricThresholdValue = $derived(Number(metricThresholdValueText));

	const hasChannel = $derived(
		usingLegacyChannel
			? channel === 'webhook'
				? webhookUrl.trim().length > 0
				: channel === 'telegram'
					? telegramBotToken.trim().length > 0 && telegramChatId.trim().length > 0
					: channel === 'email'
						? emailTo.trim().length > 0
						: pagerDutyRoutingKey.trim().length > 0
			: selectedChannelIds.length > 0
	);

	const hasCondition = $derived(
		conditionKind === 'MetricThreshold'
			? metricName.trim().length > 0 && Number.isFinite(metricThresholdValue)
			: Number.isFinite(thresholdCount) && thresholdCount > 0
	);

	const canSave = $derived(
		name.trim().length > 0 &&
			hasChannel &&
			hasCondition &&
			Number.isFinite(windowSeconds) &&
			windowSeconds > 0 &&
			Number.isFinite(cooldownSeconds) &&
			cooldownSeconds >= 0
	);

	// One-off wide-window aggregate to enumerate service names for the picker, same
	// approach LogsExplorerState.loadKnownServices uses - duplicated rather than shared
	// since the Alerts page has no LogsExplorerState instance to borrow one from.
	let knownServices = $state<string[]>([]);
	// Same wide-window discovery for the metric-name picker - getMetricNames() with no
	// filter, same "no Explorer state to borrow one from" reasoning as knownServices.
	let knownMetrics = $state<MetricNameInfo[]>([]);
	onMount(() => {
		void loadKnownServices();
		void loadKnownMetrics();
	});
	async function loadKnownServices(): Promise<void> {
		try {
			const to = new Date();
			const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
			const res = await aggregateLogs({
				filter: { from: from.toISOString(), to: to.toISOString() },
				bucketWidthSeconds: 7 * 24 * 60 * 60,
				groupBy: 'Service'
			});
			knownServices = [...new Set(res.buckets.map((b) => b.groupKey).filter((k): k is string => !!k))].sort();
		} catch {
			// Non-critical - the picker just shows fewer/no options until a retry.
		}
	}
	async function loadKnownMetrics(): Promise<void> {
		try {
			const to = new Date();
			const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
			const res = await getMetricNames({ from: from.toISOString(), to: to.toISOString() });
			// Same name can be emitted by more than one service (MetricNamesQueryBuilder's own
			// remarks) - dedupe by name for the picker, keeping the first entry's type. A
			// metric changing point type between services is not a shape this picker handles;
			// not expected in practice (one metric name -> one instrument type per app).
			const seen = new Set<string>();
			knownMetrics = res.metrics.filter((mi) => (seen.has(mi.metricName) ? false : (seen.add(mi.metricName), true)));
		} catch {
			// Non-critical - the picker just shows fewer/no options until a retry.
		}
	}
	function handleMetricNameChange(next: string): void {
		metricName = next;
		metricType = knownMetrics.find((mi) => mi.metricName === next)?.type ?? metricType;
		if (!AGGREGATIONS_BY_TYPE[metricType].includes(metricAggregation)) {
			metricAggregation = AGGREGATIONS_BY_TYPE[metricType][0];
		}
	}

	const serviceOptions = $derived(knownServices.map((s) => ({ value: s, label: s })));
	const metricNameOptions = $derived(knownMetrics.map((mi) => ({ value: mi.metricName, label: mi.metricName })));
	const aggregationOptions = $derived(AGGREGATIONS_BY_TYPE[metricType]);
	const severityOptions = $derived(SEVERITY_BUCKETS.map((b) => ({ value: b.id, label: severityBucketLabel(b) })));
	const channelOptions = $derived(channels.channels.map((c) => ({ value: c.id, label: `${c.name} (${c.type})` })));
	const selectedSeverityIds = $derived(
		SEVERITY_BUCKETS.filter((b) => severityNumbersForBucket(b).every((n) => severityNumbers.includes(n))).map((b) => b.id)
	);
	function handleSeverityChange(ids: string[]): void {
		const numbers = ids.flatMap((id) => {
			const bucket = SEVERITY_BUCKETS.find((b) => b.id === id);
			return bucket ? severityNumbersForBucket(bucket) : [];
		});
		severityNumbers = [...new Set(numbers)];
	}

	function buildRequest(): AlertRuleRequest {
		const condition: AlertRuleRequest['condition'] = {};
		if (services.length) condition.services = [...services];
		if (severityNumbers.length) condition.severityNumbers = [...severityNumbers];
		if (search.trim()) condition.search = search.trim();
		return {
			name: name.trim(),
			description: description.trim(),
			enabled,
			condition,
			// count is a placeholder (ignored server-side) when conditionKind is
			// MetricThreshold - see AlertThreshold.Count's own doc comment.
			threshold: { count: conditionKind === 'MetricThreshold' ? 0 : thresholdCount, comparator },
			windowSeconds,
			cooldownSeconds,
			// Exactly one notification mode goes out - the legacy inline fields (only when
			// usingLegacyChannel; the others left "" so the API's channel validation,
			// AlertRuleRequest.ValidateChannel, sees a clean single choice even if the user
			// typed into a field before switching the selector) or channelIds, never both.
			webhookUrl: usingLegacyChannel && channel === 'webhook' ? webhookUrl.trim() : '',
			telegramBotToken: usingLegacyChannel && channel === 'telegram' ? telegramBotToken.trim() : '',
			telegramChatId: usingLegacyChannel && channel === 'telegram' ? telegramChatId.trim() : '',
			emailTo: usingLegacyChannel && channel === 'email' ? emailTo.trim() : '',
			pagerDutyRoutingKey: usingLegacyChannel && channel === 'pagerduty' ? pagerDutyRoutingKey.trim() : '',
			channelIds: usingLegacyChannel ? [] : [...selectedChannelIds],
			conditionKind,
			// Only sent (rather than left undefined either way) when actually in metric mode -
			// same "field present, meaningful only for one mode" shape the channel fields
			// above already use.
			metricCondition:
				conditionKind === 'MetricThreshold'
					? { metricName: metricName.trim(), type: metricType, aggregation: metricAggregation }
					: undefined,
			metricThresholdValue: conditionKind === 'MetricThreshold' ? metricThresholdValue : undefined
		};
	}

	async function handleTest(): Promise<void> {
		testing = true;
		testError = null;
		try {
			testResult = await testDraftAlertRule(buildRequest());
		} catch (err) {
			testError = err instanceof Error ? err.message : String(err);
		} finally {
			testing = false;
		}
	}

	// Unlike handleTest above (a dry-run evaluation), this actually notifies through the
	// selected channel. Always goes through the unsaved-draft endpoint with the form's
	// current field values, even when editing a saved rule - so testing an in-progress
	// edit (e.g. a routing key being typed in right now) never falls back to testing the
	// still-saved value instead. AlertRuleTable.svelte's own "send test" row action covers
	// the already-saved case.
	async function handleSendTest(): Promise<void> {
		sendingTest = true;
		sendTestError = null;
		try {
			sendTestResult = await sendTestDraftAlertRule(buildRequest());
		} catch (err) {
			sendTestError = err instanceof Error ? err.message : String(err);
		} finally {
			sendingTest = false;
		}
	}

	async function handleSave(): Promise<void> {
		const target = alerts.formTarget;
		const request = buildRequest();
		if (target && target !== 'new') {
			await alerts.update(target.id, request);
		} else {
			await alerts.create(request);
		}
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && alerts.closeForm()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.alertRuleForm_titleEdit() : m.alertRuleForm_titleNew()}</Dialog.Title>
			<Dialog.Description>
				{m.alertRuleForm_description()}
			</Dialog.Description>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_nameLabel()}</span>
				<Input bind:value={name} placeholder={m.alertRuleForm_namePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_descriptionLabel()}</span>
				<Textarea bind:value={description} placeholder={m.alertRuleForm_optionalPlaceholder()} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_conditionKindLabel()}</span>
				<Select.Root type="single" value={conditionKind} onValueChange={(v) => v && (conditionKind = v as AlertConditionKind)}>
					<Select.Trigger class="w-48">
						{conditionKind === 'MetricThreshold' ? m.alertRuleForm_conditionKindMetricThreshold() : m.alertRuleForm_conditionKindLogCount()}
					</Select.Trigger>
					<Select.Content>
						<Select.Item value="LogCount" label={m.alertRuleForm_conditionKindLogCount()} />
						<Select.Item value="MetricThreshold" label={m.alertRuleForm_conditionKindMetricThreshold()} />
					</Select.Content>
				</Select.Root>
			</div>

			{#if conditionKind === 'LogCount'}
				<div class="flex flex-wrap items-center gap-2">
					<PopoverMultiSelect
						label={m.alertRuleForm_serviceLabel()}
						options={serviceOptions}
						selected={services}
						onChange={(next) => (services = next)}
					/>
					<PopoverMultiSelect
						label={m.alertRuleForm_levelLabel()}
						options={severityOptions}
						selected={selectedSeverityIds}
						onChange={handleSeverityChange}
					/>
				</div>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_searchLabel()}</span>
					<Input bind:value={search} placeholder={m.alertRuleForm_optionalPlaceholder()} />
				</div>
			{:else}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_metricNameLabel()}</span>
					<Select.Root type="single" value={metricName} onValueChange={(v) => v && handleMetricNameChange(v)}>
						<Select.Trigger class="w-full">
							{metricName || m.alertRuleForm_metricNamePlaceholder()}
						</Select.Trigger>
						<Select.Content>
							{#each metricNameOptions as option (option.value)}
								<Select.Item value={option.value} label={option.label} />
							{/each}
						</Select.Content>
					</Select.Root>
				</div>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_aggregationLabel()}</span>
					<Select.Root type="single" value={metricAggregation} onValueChange={(v) => v && (metricAggregation = v as MetricAlertAggregation)}>
						<Select.Trigger class="w-40">
							{metricAggregation}
						</Select.Trigger>
						<Select.Content>
							{#each aggregationOptions as option (option)}
								<Select.Item value={option} label={option} />
							{/each}
						</Select.Content>
					</Select.Root>
				</div>
			{/if}

			<div class="flex items-end gap-2">
				{#if conditionKind === 'LogCount'}
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.alertRuleForm_thresholdLabel()}</span>
						<Select.Root type="single" value={comparator} onValueChange={(v) => v && (comparator = v as ThresholdComparator)}>
							<Select.Trigger class="w-20">
								{comparator === 'LessThan' ? '<' : '>='}
							</Select.Trigger>
							<Select.Content>
								<Select.Item value="GreaterThanOrEqual" label=">=" />
								<Select.Item value="LessThan" label="<" />
							</Select.Content>
						</Select.Root>
					</div>
					<Input type="number" min="1" bind:value={thresholdCountText} class="w-24" />
					<span class="text-muted-foreground pb-1.5 text-xs">{m.alertRuleForm_eventsIn()}</span>
				{:else}
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.alertRuleForm_thresholdLabel()}</span>
						<Select.Root type="single" value={comparator} onValueChange={(v) => v && (comparator = v as ThresholdComparator)}>
							<Select.Trigger class="w-20">
								{comparator === 'LessThan' ? '<' : '>='}
							</Select.Trigger>
							<Select.Content>
								<Select.Item value="GreaterThanOrEqual" label=">=" />
								<Select.Item value="LessThan" label="<" />
							</Select.Content>
						</Select.Root>
					</div>
					<Input type="number" bind:value={metricThresholdValueText} class="w-24" />
					<span class="text-muted-foreground pb-1.5 text-xs">{m.alertRuleForm_metricOverLabel()}</span>
				{/if}
				<Input type="number" min="1" bind:value={windowSecondsText} class="w-24" />
				<span class="text-muted-foreground pb-1.5 text-xs">{m.alertRuleForm_seconds()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_cooldownLabel()}</span>
				<Input type="number" min="0" bind:value={cooldownSecondsText} class="w-24" />
				<span class="text-muted-foreground text-xs">{m.alertRuleForm_cooldownHint()}</span>
			</div>

			{#if usingLegacyChannel}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_notifyViaLabel()}</span>
					<Select.Root type="single" value={channel} onValueChange={(v) => v && (channel = v as typeof channel)}>
						<Select.Trigger class="w-40">
							{channel === 'telegram'
								? m.alertRuleForm_channelTelegram()
								: channel === 'email'
									? m.alertRuleForm_channelEmail()
									: channel === 'pagerduty'
										? m.alertRuleForm_channelPagerDuty()
										: m.alertRuleForm_channelWebhook()}
						</Select.Trigger>
						<Select.Content>
							<Select.Item value="webhook" label={m.alertRuleForm_channelWebhook()} />
							<Select.Item value="telegram" label={m.alertRuleForm_channelTelegram()} />
							<Select.Item value="email" label={m.alertRuleForm_channelEmail()} />
							<Select.Item value="pagerduty" label={m.alertRuleForm_channelPagerDuty()} />
						</Select.Content>
					</Select.Root>
				</div>

				{#if channel === 'webhook'}
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.alertRuleForm_webhookUrlLabel()}</span>
						<Input bind:value={webhookUrl} placeholder={m.alertRuleForm_webhookUrlPlaceholder()} />
						<span class="text-muted-foreground text-xs">{m.alertRuleForm_webhookUrlHint()}</span>
					</div>
				{:else if channel === 'telegram'}
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.alertRuleForm_botTokenLabel()}</span>
						<Input bind:value={telegramBotToken} placeholder={m.alertRuleForm_botTokenPlaceholder()} />
						<span class="text-muted-foreground text-xs">{m.alertRuleForm_botTokenHint()}</span>
					</div>
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.alertRuleForm_chatIdLabel()}</span>
						<Input bind:value={telegramChatId} placeholder={m.alertRuleForm_chatIdPlaceholder()} />
						<span class="text-muted-foreground text-xs">
							{m.alertRuleForm_chatIdHint()}
						</span>
					</div>
				{:else if channel === 'email'}
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.alertRuleForm_emailToLabel()}</span>
						<Input bind:value={emailTo} placeholder={m.alertRuleForm_emailToPlaceholder()} />
						<span class="text-muted-foreground text-xs">
							{m.alertRuleForm_emailToHint()}
						</span>
					</div>
				{:else}
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.alertRuleForm_pagerDutyRoutingKeyLabel()}</span>
						<Input bind:value={pagerDutyRoutingKey} placeholder={m.alertRuleForm_pagerDutyRoutingKeyPlaceholder()} />
						<span class="text-muted-foreground text-xs">
							{m.alertRuleForm_pagerDutyRoutingKeyHint()}
						</span>
					</div>
				{/if}
			{:else}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_channelsLabel()}</span>
					<PopoverMultiSelect
						label={m.alertRuleForm_channelsLabel()}
						options={channelOptions}
						selected={selectedChannelIds}
						onChange={(next) => (selectedChannelIds = next)}
					/>
					{#if channels.channels.length === 0}
						<span class="text-muted-foreground text-xs">{m.alertRuleForm_channelsEmptyHint()}</span>
					{/if}
				</div>
			{/if}

			<div class="flex items-center gap-2">
				<Switch bind:checked={enabled} />
				<span class="text-xs">{m.alertRuleForm_enabledLabel()}</span>
			</div>

			<div class="flex flex-wrap items-center gap-2 border-t pt-3">
				<Button variant="outline" size="sm" onclick={handleTest} disabled={testing}>
					{#if testing}
						<Spinner class="size-3.5" />
					{/if}
					{m.alertRuleForm_testButton()}
				</Button>
				{#if testResult}
					<Badge variant={testResult.wouldFire ? 'warning' : 'outline'}>
						{#if testResult.conditionKind === 'MetricThreshold'}
							{testResult.wouldFire
								? m.alertRuleForm_testResultFiringMetric({ value: testResult.observedValue ?? 0 })
								: m.alertRuleForm_testResultNotFiringMetric({ value: testResult.observedValue ?? 0 })}
						{:else}
							{testResult.wouldFire
								? m.alertRuleForm_testResultFiring({ count: testResult.observedCount })
								: m.alertRuleForm_testResultNotFiring({ count: testResult.observedCount })}
						{/if}
					</Badge>
				{:else if testError}
					<span class="text-destructive text-xs">{testError}</span>
				{/if}

				<Button variant="outline" size="sm" onclick={handleSendTest} disabled={sendingTest || !hasChannel}>
					{#if sendingTest}
						<Spinner class="size-3.5" />
					{/if}
					{m.alertRuleForm_sendTestButton()}
				</Button>
				{#if sendTestResult}
					<Badge variant={sendTestResult.success ? 'secondary' : 'destructive'}>
						{sendTestResult.success ? m.alertRuleForm_sendTestSuccess() : m.alertRuleForm_sendTestFailure({ error: sendTestResult.error })}
					</Badge>
				{:else if sendTestError}
					<span class="text-destructive text-xs">{sendTestError}</span>
				{/if}
			</div>

			{#if alerts.saveError}
				<p class="text-destructive text-xs">{alerts.saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => alerts.closeForm()}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={handleSave} disabled={!canSave || alerts.saving}>
				{#if alerts.saving}
					<Spinner class="size-3.5" />
				{/if}
				{isEdit ? m.alertRuleForm_saveChanges() : m.alertRuleForm_createAlert()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
