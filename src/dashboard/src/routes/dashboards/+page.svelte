<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { DashboardsState } from '#lib/dashboards/state.svelte.ts';
	import { dashboardsContext } from '#lib/dashboards/context.ts';
	import DashboardTable from '#lib/components/dashboards/DashboardTable.svelte';
	import DashboardFormDialog from '#lib/components/dashboards/DashboardFormDialog.svelte';
	import * as m from '#lib/paraglide/messages.js';
	import { startPageTour } from '#lib/tour/page.ts';
	import { dashboardsTour } from '#lib/tour/tours/dashboards.ts';

	const dashboards = dashboardsContext.set(new DashboardsState());

	let stopTour: (() => void) | undefined;

	onMount(() => {
		stopTour = startPageTour(dashboardsTour);
		void dashboards.load();
	});

	onDestroy(() => stopTour?.());
</script>

<svelte:head>
	<title>{m.dashboardsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<DashboardTable />
</div>
<DashboardFormDialog />
