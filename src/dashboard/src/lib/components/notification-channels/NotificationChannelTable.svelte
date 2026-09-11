<script lang="ts">
	// Mirrors AlertRuleTable.svelte's shape - same Table/Empty/Badge/Spinner components,
	// same per-row ephemeral "send test" result state.
	import * as Table from '$lib/components/ui/table';
	import * as Empty from '$lib/components/ui/empty';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { Spinner } from '$lib/components/ui/spinner';
	import { notificationChannelsContext } from '$lib/notification-channels/context';
	import type { NotificationChannel } from '$lib/notification-channels-api';
	import * as m from '$lib/paraglide/messages';
	import PlusIcon from '@lucide/svelte/icons/plus';
	import PencilIcon from '@lucide/svelte/icons/pencil';
	import Trash2Icon from '@lucide/svelte/icons/trash-2';
	import SendIcon from '@lucide/svelte/icons/send';
	import BellIcon from '@lucide/svelte/icons/bell';

	const channels = notificationChannelsContext.get();

	// A redacted one-line summary of the channel's destination - never the raw secret
	// (bot token/routing key) in full, same "don't echo a live credential back onto the
	// screen at rest" caution the access-tokens page's own table already follows for a
	// token's raw value.
	function destinationSummary(channel: NotificationChannel): string {
		switch (channel.type) {
			case 'Webhook':
				return channel.webhookUrl;
			case 'Telegram':
				return channel.telegramChatId ? `chat ${channel.telegramChatId}` : '';
			case 'Email':
				return channel.emailTo;
			case 'PagerDuty':
				return channel.pagerDutyRoutingKey ? `${channel.pagerDutyRoutingKey.slice(0, 6)}…` : '';
		}
	}

	async function handleDelete(channel: NotificationChannel): Promise<void> {
		if (!confirm(m.notificationChannelTable_deleteConfirm({ name: channel.name }))) return;
		await channels.remove(channel.id);
	}
</script>

<div class="flex items-center justify-between border-b px-4 py-3">
	<div>
		<h1 class="text-sm font-semibold">{m.notificationChannelTable_heading()}</h1>
		<p class="text-muted-foreground text-xs">{m.notificationChannelTable_description()}</p>
	</div>
	<Button size="sm" onclick={() => channels.openCreate()}>
		<PlusIcon data-icon="inline-start" />
		{m.notificationChannelTable_newChannel()}
	</Button>
</div>

{#if channels.loading}
	<div class="flex flex-1 items-center justify-center">
		<Spinner />
	</div>
{:else if channels.error}
	<div class="flex flex-1 items-center justify-center">
		<p class="text-destructive text-sm">{channels.error}</p>
	</div>
{:else if channels.channels.length === 0}
	<Empty.Root class="flex-1">
		<Empty.Header>
			<Empty.Media>
				<BellIcon class="text-muted-foreground size-8" />
			</Empty.Media>
			<Empty.Title>{m.notificationChannelTable_emptyTitle()}</Empty.Title>
			<Empty.Description>{m.notificationChannelTable_emptyDescription()}</Empty.Description>
		</Empty.Header>
		<Empty.Content>
			<Button size="sm" onclick={() => channels.openCreate()}>
				<PlusIcon data-icon="inline-start" />
				{m.notificationChannelTable_newChannel()}
			</Button>
		</Empty.Content>
	</Empty.Root>
{:else}
	<div class="min-h-0 flex-1 overflow-y-auto">
		<Table.Root>
			<Table.Header>
				<Table.Row>
					<Table.Head>{m.notificationChannelTable_colName()}</Table.Head>
					<Table.Head>{m.notificationChannelTable_colType()}</Table.Head>
					<Table.Head>{m.notificationChannelTable_colDestination()}</Table.Head>
					<Table.Head class="text-right">{m.notificationChannelTable_colActions()}</Table.Head>
				</Table.Row>
			</Table.Header>
			<Table.Body>
				{#each channels.channels as channel (channel.id)}
					<Table.Row>
						<Table.Cell class="font-medium">
							{channel.name}
							{#if channel.description}
								<p class="text-muted-foreground font-normal">{channel.description}</p>
							{/if}
						</Table.Cell>
						<Table.Cell><Badge variant="outline">{channel.type}</Badge></Table.Cell>
						<Table.Cell class="text-muted-foreground font-mono text-xs">
							{destinationSummary(channel)}
							{#if channels.testResult?.id === channel.id}
								<Badge variant={channels.testResult.success ? 'secondary' : 'destructive'} class="ml-1 font-sans">
									{channels.testResult.success ? m.notificationChannelTable_testSent() : m.notificationChannelTable_testFailed()}
								</Badge>
							{/if}
						</Table.Cell>
						<Table.Cell class="text-right">
							<Button
								variant="ghost"
								size="icon-sm"
								title={m.notificationChannelTable_actionSendTest()}
								disabled={channels.testingId === channel.id}
								onclick={() => channels.sendTest(channel.id)}
							>
								{#if channels.testingId === channel.id}
									<Spinner class="size-3.5" />
								{:else}
									<SendIcon />
								{/if}
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								title={m.notificationChannelTable_actionEdit()}
								onclick={() => channels.openEdit(channel)}
							>
								<PencilIcon />
							</Button>
							<Button
								variant="ghost"
								size="icon-sm"
								class="text-destructive hover:text-destructive"
								title={m.notificationChannelTable_actionDelete()}
								onclick={() => handleDelete(channel)}
							>
								<Trash2Icon />
							</Button>
						</Table.Cell>
					</Table.Row>
				{/each}
			</Table.Body>
		</Table.Root>
	</div>
{/if}
