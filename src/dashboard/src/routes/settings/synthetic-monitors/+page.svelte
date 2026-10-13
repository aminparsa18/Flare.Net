<script lang="ts">
	import { onMount } from 'svelte';
	import { SyntheticMonitorsState } from '#lib/synthetic-monitors/state.svelte.ts';
	import { syntheticMonitorsContext } from '#lib/synthetic-monitors/context.ts';
	import SyntheticMonitorTable from '#lib/components/synthetic-monitors/SyntheticMonitorTable.svelte';
	import SyntheticMonitorFormDialog from '#lib/components/synthetic-monitors/SyntheticMonitorFormDialog.svelte';
	import * as m from '#lib/paraglide/messages.js';

	const monitors = syntheticMonitorsContext.set(new SyntheticMonitorsState());

	onMount(() => {
		void monitors.load();
	});
</script>

<svelte:head>
	<title>{m.synthetic_heading()}</title>
</svelte:head>

<SyntheticMonitorTable />
<SyntheticMonitorFormDialog />
