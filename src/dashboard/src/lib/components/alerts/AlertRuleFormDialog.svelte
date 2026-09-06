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
	import { testDraftAlertRule, type AlertRuleRequest, type ThresholdComparator, type AlertTestResult } from '$lib/alerts-api';
	import { aggregateLogs } from '$lib/api';
	import { SEVERITY_BUCKETS, severityNumbersForBucket } from '$lib/logs/severity';
	import * as m from '$lib/paraglide/messages';

	const alerts = alertsContext.get();

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
	let channel = $state<'webhook' | 'telegram' | 'email'>('webhook');
	let webhookUrl = $state('');
	let telegramBotToken = $state('');
	let telegramChatId = $state('');
	let emailTo = $state('');

	let testResult = $state<AlertTestResult | null>(null);
	let testing = $state(false);
	let testError = $state<string | null>(null);

	// Resets the draft whenever the dialog opens for a different target - `formTarget`
	// only ever transitions null <-> 'new'|AlertRule at open/close time, so this doesn't
	// fight the user's in-progress edits on every keystroke.
	$effect(() => {
		const target = alerts.formTarget;
		testResult = null;
		testError = null;
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
			channel = target.telegramBotToken || target.telegramChatId ? 'telegram' : target.emailTo ? 'email' : 'webhook';
			webhookUrl = target.webhookUrl;
			telegramBotToken = target.telegramBotToken;
			telegramChatId = target.telegramChatId;
			emailTo = target.emailTo;
		}
	});

	const thresholdCount = $derived(Number(thresholdCountText));
	const windowSeconds = $derived(Number(windowSecondsText));
	const cooldownSeconds = $derived(Number(cooldownSecondsText));

	const hasChannel = $derived(
		channel === 'webhook'
			? webhookUrl.trim().length > 0
			: channel === 'telegram'
				? telegramBotToken.trim().length > 0 && telegramChatId.trim().length > 0
				: emailTo.trim().length > 0
	);

	const canSave = $derived(
		name.trim().length > 0 &&
			hasChannel &&
			Number.isFinite(thresholdCount) &&
			thresholdCount > 0 &&
			Number.isFinite(windowSeconds) &&
			windowSeconds > 0 &&
			Number.isFinite(cooldownSeconds) &&
			cooldownSeconds >= 0
	);

	// One-off wide-window aggregate to enumerate service names for the picker, same
	// approach LogsExplorerState.loadKnownServices uses - duplicated rather than shared
	// since the Alerts page has no LogsExplorerState instance to borrow one from.
	let knownServices = $state<string[]>([]);
	onMount(() => {
		void loadKnownServices();
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

	const serviceOptions = $derived(knownServices.map((s) => ({ value: s, label: s })));
	const severityOptions = SEVERITY_BUCKETS.map((b) => ({ value: b.label, label: b.label }));
	const selectedSeverityLabels = $derived(
		SEVERITY_BUCKETS.filter((b) => severityNumbersForBucket(b).every((n) => severityNumbers.includes(n))).map((b) => b.label)
	);
	function handleSeverityChange(labels: string[]): void {
		const numbers = labels.flatMap((l) => {
			const bucket = SEVERITY_BUCKETS.find((b) => b.label === l);
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
			threshold: { count: thresholdCount, comparator },
			windowSeconds,
			cooldownSeconds,
			// Exactly one channel goes out non-blank - the others are left "" so the API's
			// channel validation (AlertRuleRequest.ValidateChannel) sees a clean single
			// choice even if the user typed into a field before switching the selector.
			webhookUrl: channel === 'webhook' ? webhookUrl.trim() : '',
			telegramBotToken: channel === 'telegram' ? telegramBotToken.trim() : '',
			telegramChatId: channel === 'telegram' ? telegramChatId.trim() : '',
			emailTo: channel === 'email' ? emailTo.trim() : ''
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
					selected={selectedSeverityLabels}
					onChange={handleSeverityChange}
				/>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_searchLabel()}</span>
				<Input bind:value={search} placeholder={m.alertRuleForm_optionalPlaceholder()} />
			</div>

			<div class="flex items-end gap-2">
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
				<Input type="number" min="1" bind:value={windowSecondsText} class="w-24" />
				<span class="text-muted-foreground pb-1.5 text-xs">{m.alertRuleForm_seconds()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_cooldownLabel()}</span>
				<Input type="number" min="0" bind:value={cooldownSecondsText} class="w-24" />
				<span class="text-muted-foreground text-xs">{m.alertRuleForm_cooldownHint()}</span>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_notifyViaLabel()}</span>
				<Select.Root type="single" value={channel} onValueChange={(v) => v && (channel = v as typeof channel)}>
					<Select.Trigger class="w-40">
						{channel === 'telegram'
							? m.alertRuleForm_channelTelegram()
							: channel === 'email'
								? m.alertRuleForm_channelEmail()
								: m.alertRuleForm_channelWebhook()}
					</Select.Trigger>
					<Select.Content>
						<Select.Item value="webhook" label={m.alertRuleForm_channelWebhook()} />
						<Select.Item value="telegram" label={m.alertRuleForm_channelTelegram()} />
						<Select.Item value="email" label={m.alertRuleForm_channelEmail()} />
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
			{:else}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_emailToLabel()}</span>
					<Input bind:value={emailTo} placeholder={m.alertRuleForm_emailToPlaceholder()} />
					<span class="text-muted-foreground text-xs">
						{m.alertRuleForm_emailToHint()}
					</span>
				</div>
			{/if}

			<div class="flex items-center gap-2">
				<Switch bind:checked={enabled} />
				<span class="text-xs">{m.alertRuleForm_enabledLabel()}</span>
			</div>

			<div class="flex items-center gap-2 border-t pt-3">
				<Button variant="outline" size="sm" onclick={handleTest} disabled={testing}>
					{#if testing}
						<Spinner class="size-3.5" />
					{/if}
					{m.alertRuleForm_testButton()}
				</Button>
				{#if testResult}
					<Badge variant={testResult.wouldFire ? 'warning' : 'outline'}>
						{testResult.wouldFire
							? m.alertRuleForm_testResultFiring({ count: testResult.observedCount })
							: m.alertRuleForm_testResultNotFiring({ count: testResult.observedCount })}
					</Badge>
				{:else if testError}
					<span class="text-destructive text-xs">{testError}</span>
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
