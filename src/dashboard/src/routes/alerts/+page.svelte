<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { AlertsState } from '$lib/alerts/state.svelte';
	import { alertsContext } from '$lib/alerts/context';
	import { parseAlertDeepLinkParams } from '$lib/deep-links';
	import { NotificationChannelsState } from '$lib/notification-channels/state.svelte';
	import { notificationChannelsContext } from '$lib/notification-channels/context';
	import { OnCallRotationsState } from '$lib/oncall-rotations/state.svelte';
	import { onCallRotationsContext } from '$lib/oncall-rotations/context';
	import { MaintenanceWindowsState } from '$lib/maintenance-windows/state.svelte';
	import { maintenanceWindowsContext } from '$lib/maintenance-windows/context';
	import AlertRuleTable from '$lib/components/alerts/AlertRuleTable.svelte';
	import AlertRuleFormDialog from '$lib/components/alerts/AlertRuleFormDialog.svelte';
	import AlertHistorySheet from '$lib/components/alerts/AlertHistorySheet.svelte';
	import NotificationChannelFormDialog from '$lib/components/notification-channels/NotificationChannelFormDialog.svelte';
	import MaintenanceWindowFormDialog from '$lib/components/maintenance-windows/MaintenanceWindowFormDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	// Channels and maintenance windows are managed under Settings > Workspace, but their state
	// is still provided here: AlertRuleFormDialog's channel picker needs the loaded channels, and
	// the Rules table badges rules an active maintenance window is muting.
	const alerts = alertsContext.set(new AlertsState());
	const channels = notificationChannelsContext.set(new NotificationChannelsState());
	const maintenance = maintenanceWindowsContext.set(new MaintenanceWindowsState());
	// The rule form's escalation section picks from the loaded rotations.
	const rotations = onCallRotationsContext.set(new OnCallRotationsState());

	onMount(() => {
		void channels.load();
		void maintenance.load();
		void rotations.load();

		// "Create alert" from a dashboard Logs/Metrics panel (DashboardPanelCard.svelte) -
		// checked before the rule list even loads (unlike ?rule= below, which needs
		// alerts.rules populated first) since opening the create dialog doesn't depend on
		// any saved rule existing. See $lib/deep-links.ts's own remarks for the param shape.
		const draft = parseAlertDeepLinkParams(page.url);
		if (draft) alerts.openCreateFromDraft(draft);

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
	<AlertRuleTable />
</div>
<AlertRuleFormDialog />
<AlertHistorySheet />
<NotificationChannelFormDialog />
<MaintenanceWindowFormDialog />
