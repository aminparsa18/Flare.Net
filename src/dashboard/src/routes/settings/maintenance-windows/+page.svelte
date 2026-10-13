<script lang="ts">
	import { onMount } from 'svelte';
	import { MaintenanceWindowsState } from '#lib/maintenance-windows/state.svelte.ts';
	import { maintenanceWindowsContext } from '#lib/maintenance-windows/context.ts';
	import MaintenanceWindowTable from '#lib/components/maintenance-windows/MaintenanceWindowTable.svelte';
	import MaintenanceWindowFormDialog from '#lib/components/maintenance-windows/MaintenanceWindowFormDialog.svelte';
	import * as m from '#lib/paraglide/messages.js';

	const maintenance = maintenanceWindowsContext.set(new MaintenanceWindowsState());

	onMount(() => {
		void maintenance.load();
	});
</script>

<svelte:head>
	<title>{m.maintenanceWindowTable_heading()}</title>
</svelte:head>

<MaintenanceWindowTable />
<MaintenanceWindowFormDialog />
