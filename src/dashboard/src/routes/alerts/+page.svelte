<script lang="ts">
	import { onMount } from 'svelte';
	import { AlertsState } from '$lib/alerts/state.svelte';
	import { alertsContext } from '$lib/alerts/context';
	import AlertRuleTable from '$lib/components/alerts/AlertRuleTable.svelte';
	import AlertRuleFormDialog from '$lib/components/alerts/AlertRuleFormDialog.svelte';
	import AlertHistorySheet from '$lib/components/alerts/AlertHistorySheet.svelte';

	const alerts = alertsContext.set(new AlertsState());

	onMount(() => {
		void alerts.load();
	});
</script>

<svelte:head>
	<title>Flare — Alerts</title>
</svelte:head>

<div class="flex h-full flex-col">
	<AlertRuleTable />
</div>
<AlertRuleFormDialog />
<AlertHistorySheet />
