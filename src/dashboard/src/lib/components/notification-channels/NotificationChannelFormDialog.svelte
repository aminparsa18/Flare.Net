<script lang="ts">
	// Create/edit a notification channel. Mirrors AlertRuleFormDialog.svelte's shape
	// (Dialog, same reset-on-open $effect pattern) - the destination fields themselves
	// reuse AlertRuleFormDialog's own `alertRuleForm_webhookUrlLabel`/etc. messages
	// directly rather than duplicating them under a new key: same fields, same wording,
	// now on a standalone entity instead of inlined on a rule.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { notificationChannelsContext } from '$lib/notification-channels/context';
	import { sendTestNotificationChannel, type NotificationChannelRequest, type NotificationChannelType } from '$lib/notification-channels-api';
	import type { AlertNotificationTestResult } from '$lib/alerts-api';
	import * as m from '$lib/paraglide/messages';

	const channels = notificationChannelsContext.get();

	const open = $derived(channels.formTarget !== null);
	const isEdit = $derived(channels.formTarget !== null && channels.formTarget !== 'new');

	let name = $state('');
	let description = $state('');
	let type = $state<NotificationChannelType>('Webhook');
	let webhookUrl = $state('');
	let telegramBotToken = $state('');
	let telegramChatId = $state('');
	let emailTo = $state('');
	let pagerDutyRoutingKey = $state('');

	let sendTestResult = $state<AlertNotificationTestResult | null>(null);
	let sendingTest = $state(false);
	let sendTestError = $state<string | null>(null);

	$effect(() => {
		const target = channels.formTarget;
		sendTestResult = null;
		sendTestError = null;
		if (target === 'new') {
			name = '';
			description = '';
			type = 'Webhook';
			webhookUrl = '';
			telegramBotToken = '';
			telegramChatId = '';
			emailTo = '';
			pagerDutyRoutingKey = '';
		} else if (target) {
			name = target.name;
			description = target.description;
			type = target.type;
			webhookUrl = target.webhookUrl;
			telegramBotToken = target.telegramBotToken;
			telegramChatId = target.telegramChatId;
			emailTo = target.emailTo;
			pagerDutyRoutingKey = target.pagerDutyRoutingKey;
		}
	});

	const hasDestination = $derived(
		type === 'Webhook'
			? webhookUrl.trim().length > 0
			: type === 'Telegram'
				? telegramBotToken.trim().length > 0 && telegramChatId.trim().length > 0
				: type === 'Email'
					? emailTo.trim().length > 0
					: pagerDutyRoutingKey.trim().length > 0
	);

	const canSave = $derived(name.trim().length > 0 && hasDestination);

	function buildRequest(): NotificationChannelRequest {
		return {
			name: name.trim(),
			description: description.trim(),
			type,
			// Exactly the field(s) matching `type` go out non-blank - the others left "" so
			// the API's destination validation (NotificationChannelRequest.ValidateDestination)
			// sees a clean single choice even if the user typed into a field before
			// switching the type selector.
			webhookUrl: type === 'Webhook' ? webhookUrl.trim() : '',
			telegramBotToken: type === 'Telegram' ? telegramBotToken.trim() : '',
			telegramChatId: type === 'Telegram' ? telegramChatId.trim() : '',
			emailTo: type === 'Email' ? emailTo.trim() : '',
			pagerDutyRoutingKey: type === 'PagerDuty' ? pagerDutyRoutingKey.trim() : ''
		};
	}

	// Only meaningful once the channel is saved (send-test needs a real id) - disabled
	// while creating, same as AlertRuleTable's row-scoped send-test has nothing to test
	// until AlertRuleFormDialog's own draft send-test endpoint is used instead. A channel
	// draft has no such draft endpoint (send-test is always against a saved channel, see
	// NotificationChannelEndpoints), so this button only appears once editing a saved one.
	async function handleSendTest(): Promise<void> {
		const target = channels.formTarget;
		if (!target || target === 'new') return;
		sendingTest = true;
		sendTestError = null;
		try {
			sendTestResult = await sendTestNotificationChannel(target.id);
		} catch (err) {
			sendTestError = err instanceof Error ? err.message : String(err);
		} finally {
			sendingTest = false;
		}
	}

	async function handleSave(): Promise<void> {
		const target = channels.formTarget;
		const request = buildRequest();
		if (target && target !== 'new') {
			await channels.update(target.id, request);
		} else {
			await channels.create(request);
		}
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && channels.closeForm()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.notificationChannelForm_titleEdit() : m.notificationChannelForm_titleNew()}</Dialog.Title>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_nameLabel()}</span>
				<Input bind:value={name} placeholder={m.notificationChannelForm_namePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_descriptionLabel()}</span>
				<Textarea bind:value={description} placeholder={m.alertRuleForm_optionalPlaceholder()} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.notificationChannelForm_typeLabel()}</span>
				<Select.Root type="single" value={type} onValueChange={(v) => v && (type = v as NotificationChannelType)}>
					<Select.Trigger class="w-40">
						{type === 'Telegram'
							? m.alertRuleForm_channelTelegram()
							: type === 'Email'
								? m.alertRuleForm_channelEmail()
								: type === 'PagerDuty'
									? m.alertRuleForm_channelPagerDuty()
									: m.alertRuleForm_channelWebhook()}
					</Select.Trigger>
					<Select.Content>
						<Select.Item value="Webhook" label={m.alertRuleForm_channelWebhook()} />
						<Select.Item value="Telegram" label={m.alertRuleForm_channelTelegram()} />
						<Select.Item value="Email" label={m.alertRuleForm_channelEmail()} />
						<Select.Item value="PagerDuty" label={m.alertRuleForm_channelPagerDuty()} />
					</Select.Content>
				</Select.Root>
			</div>

			{#if type === 'Webhook'}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_webhookUrlLabel()}</span>
					<Input bind:value={webhookUrl} placeholder={m.alertRuleForm_webhookUrlPlaceholder()} />
					<span class="text-muted-foreground text-xs">{m.alertRuleForm_webhookUrlHint()}</span>
				</div>
			{:else if type === 'Telegram'}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_botTokenLabel()}</span>
					<Input bind:value={telegramBotToken} placeholder={m.alertRuleForm_botTokenPlaceholder()} />
					<span class="text-muted-foreground text-xs">{m.alertRuleForm_botTokenHint()}</span>
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_chatIdLabel()}</span>
					<Input bind:value={telegramChatId} placeholder={m.alertRuleForm_chatIdPlaceholder()} />
					<span class="text-muted-foreground text-xs">{m.alertRuleForm_chatIdHint()}</span>
				</div>
			{:else if type === 'Email'}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_emailToLabel()}</span>
					<Input bind:value={emailTo} placeholder={m.alertRuleForm_emailToPlaceholder()} />
					<span class="text-muted-foreground text-xs">{m.alertRuleForm_emailToHint()}</span>
				</div>
			{:else}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.alertRuleForm_pagerDutyRoutingKeyLabel()}</span>
					<Input bind:value={pagerDutyRoutingKey} placeholder={m.alertRuleForm_pagerDutyRoutingKeyPlaceholder()} />
					<span class="text-muted-foreground text-xs">{m.alertRuleForm_pagerDutyRoutingKeyHint()}</span>
				</div>
			{/if}

			{#if isEdit}
				<div class="flex flex-wrap items-center gap-2 border-t pt-3">
					<Button variant="outline" size="sm" onclick={handleSendTest} disabled={sendingTest}>
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
			{/if}

			{#if channels.saveError}
				<p class="text-destructive text-xs">{channels.saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => channels.closeForm()}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={handleSave} disabled={!canSave || channels.saving}>
				{#if channels.saving}
					<Spinner class="size-3.5" />
				{/if}
				{isEdit ? m.alertRuleForm_saveChanges() : m.notificationChannelForm_createChannel()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
