<script lang="ts">
	import { onMount } from 'svelte';
	import { DashboardsState } from '#lib/dashboards/state.svelte.ts';
	import { dashboardsContext } from '#lib/dashboards/context.ts';
	import DashboardTable from '#lib/components/dashboards/DashboardTable.svelte';
	import DashboardFormDialog from '#lib/components/dashboards/DashboardFormDialog.svelte';
	import * as m from '#lib/paraglide/messages.js';

	const dashboards = dashboardsContext.set(new DashboardsState());

	onMount(() => {
		void dashboards.load();
	});
</script>

<svelte:head>
	<title>{m.dashboardsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<DashboardTable />
</div>
<DashboardFormDialog />
