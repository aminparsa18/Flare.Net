<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { AlertsState } from '$lib/alerts/state.svelte';
	import { alertsContext } from '$lib/alerts/context';
	import { NotificationChannelsState } from '$lib/notification-channels/state.svelte';
	import { notificationChannelsContext } from '$lib/notification-channels/context';
	import { Button } from '$lib/components/ui/button';
	import AlertRuleTable from '$lib/components/alerts/AlertRuleTable.svelte';
	import AlertRuleFormDialog from '$lib/components/alerts/AlertRuleFormDialog.svelte';
	import AlertHistorySheet from '$lib/components/alerts/AlertHistorySheet.svelte';
	import NotificationChannelTable from '$lib/components/notification-channels/NotificationChannelTable.svelte';
	import NotificationChannelFormDialog from '$lib/components/notification-channels/NotificationChannelFormDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	// Notification channels (docs-internal/adr/0021-reusable-notification-channels.md)
	// live as a second tab on this same page rather than their own top-level route/nav
	// entry - deliberately not surfaced as its own thing to navigate to on its own; a
	// channel only matters in service of an alert rule. Both states are provided here
	// (not just the active tab's) since AlertRuleFormDialog's channel picker needs
	// NotificationChannelsState-loaded data even while the Rules tab is showing.
	const alerts = alertsContext.set(new AlertsState());
	const channels = notificationChannelsContext.set(new NotificationChannelsState());

	let tab = $state<'rules' | 'channels'>('rules');

	onMount(() => {
		void channels.load();
		void (async () => {
			await alerts.load();

			// ?rule=<id> - the deep link a fired alert's notification (Slack/Telegram/
			// email/PagerDuty) carries back into Flare (see AlertMessageFormatter.BuildRuleUrl
			// on the API side). Opens that rule's history sheet, the natural "what fired
			// and when" landing view for someone arriving from a notification, same as
			// clicking the history action in AlertRuleTable would. Silently does nothing
			// for an unknown/already-deleted rule id rather than showing an error - the
			// rule list itself still loads and is usable either way.
			const ruleId = page.url.searchParams.get('rule');
			if (!ruleId) return;
			const rule = alerts.rules.find((r) => r.id === ruleId);
			if (rule) alerts.openHistory(rule);
		})();
	});
</script>

<svelte:head>
	<title>{m.alertsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<div class="flex items-center gap-1 border-b px-4 py-2">
		<Button variant={tab === 'rules' ? 'secondary' : 'ghost'} size="sm" onclick={() => (tab = 'rules')}>
			{m.nav_alerts()}
		</Button>
		<Button variant={tab === 'channels' ? 'secondary' : 'ghost'} size="sm" onclick={() => (tab = 'channels')}>
			{m.notificationChannelTable_heading()}
		</Button>
	</div>
	{#if tab === 'rules'}
		<AlertRuleTable />
	{:else}
		<NotificationChannelTable />
	{/if}
</div>
<AlertRuleFormDialog />
<AlertHistorySheet />
<NotificationChannelFormDialog />
