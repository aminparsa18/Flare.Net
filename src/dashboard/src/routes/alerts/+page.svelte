<script lang="ts">
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { AlertsState } from '$lib/alerts/state.svelte';
	import { alertsContext } from '$lib/alerts/context';
	import AlertRuleTable from '$lib/components/alerts/AlertRuleTable.svelte';
	import AlertRuleFormDialog from '$lib/components/alerts/AlertRuleFormDialog.svelte';
	import AlertHistorySheet from '$lib/components/alerts/AlertHistorySheet.svelte';
	import * as m from '$lib/paraglide/messages';

	const alerts = alertsContext.set(new AlertsState());

	onMount(() => {
		void (async () => {
			await alerts.load();

			// ?rule=<id> - the deep link a fired alert's notification (Slack/Telegram/email/
			// PagerDuty) carries back into Flare (see AlertMessageFormatter.BuildRuleUrl on
			// the API side). Opens that rule's history sheet, the natural "what fired and
			// when" landing view for someone arriving from a notification, same as clicking
			// the history action in AlertRuleTable would. Silently does nothing for an
			// unknown/already-deleted rule id rather than showing an error - the rule list
			// itself still loads and is usable either way.
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
