<script lang="ts">
	import { onMount } from 'svelte';
	import { SyntheticMonitorsState } from '$lib/synthetic-monitors/state.svelte';
	import { syntheticMonitorsContext } from '$lib/synthetic-monitors/context';
	import SyntheticMonitorTable from '$lib/components/synthetic-monitors/SyntheticMonitorTable.svelte';
	import SyntheticMonitorFormDialog from '$lib/components/synthetic-monitors/SyntheticMonitorFormDialog.svelte';
	import * as m from '$lib/paraglide/messages';

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
