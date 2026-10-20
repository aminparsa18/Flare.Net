<script lang="ts">
	import { onMount } from 'svelte';
	import { CheckInMonitorsState } from '#lib/checkin-monitors/state.svelte.ts';
	import { checkInMonitorsContext } from '#lib/checkin-monitors/context.ts';
	import CheckInMonitorTable from '#lib/components/checkin-monitors/CheckInMonitorTable.svelte';
	import CheckInMonitorFormDialog from '#lib/components/checkin-monitors/CheckInMonitorFormDialog.svelte';
	import * as m from '#lib/paraglide/messages.js';

	const monitors = checkInMonitorsContext.set(new CheckInMonitorsState());

	onMount(() => {
		void monitors.load();
	});
</script>

<svelte:head>
	<title>{m.checkin_heading()}</title>
</svelte:head>

<CheckInMonitorTable />
<CheckInMonitorFormDialog />
