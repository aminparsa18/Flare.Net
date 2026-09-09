<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import { ServicesState } from '$lib/services/state.svelte';
	import { servicesContext } from '$lib/services/context';
	import ServicesToolbar from '$lib/components/services/ServicesToolbar.svelte';
	import ServicesSummaryTiles from '$lib/components/services/ServicesSummaryTiles.svelte';
	import ServicesTable from '$lib/components/services/ServicesTable.svelte';
	import * as m from '$lib/paraglide/messages';

	const services = servicesContext.set(new ServicesState());

	onMount(() => {
		void services.load();
		services.startPolling();
	});

	onDestroy(() => {
		services.dispose();
	});
</script>

<svelte:head>
	<title>{m.servicesPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<ServicesToolbar />
	<div class="flex min-h-0 flex-1 flex-col overflow-auto">
		{#if services.error}
			<p class="text-destructive px-4 py-3 text-sm">{services.error}</p>
		{/if}
		<ServicesSummaryTiles />
		<ServicesTable />
	</div>
</div>
