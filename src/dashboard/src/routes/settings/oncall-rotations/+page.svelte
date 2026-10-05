<script lang="ts">
	import { onMount } from 'svelte';
	import { OnCallRotationsState } from '$lib/oncall-rotations/state.svelte';
	import { onCallRotationsContext } from '$lib/oncall-rotations/context';
	import { NotificationChannelsState } from '$lib/notification-channels/state.svelte';
	import { notificationChannelsContext } from '$lib/notification-channels/context';
	import OnCallRotationTable from '$lib/components/oncall-rotations/OnCallRotationTable.svelte';
	import OnCallRotationFormDialog from '$lib/components/oncall-rotations/OnCallRotationFormDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	const rotations = onCallRotationsContext.set(new OnCallRotationsState());
	// Participants are notification channels, so the table and form need them loaded.
	const channels = notificationChannelsContext.set(new NotificationChannelsState());

	onMount(() => {
		void channels.load();
		void rotations.load();
	});
</script>

<svelte:head>
	<title>{m.oncall_heading()}</title>
</svelte:head>

<OnCallRotationTable />
<OnCallRotationFormDialog />
