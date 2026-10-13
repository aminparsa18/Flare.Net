<script lang="ts">
	import { onMount } from 'svelte';
	import { NotificationChannelsState } from '#lib/notification-channels/state.svelte.ts';
	import { notificationChannelsContext } from '#lib/notification-channels/context.ts';
	import NotificationChannelTable from '#lib/components/notification-channels/NotificationChannelTable.svelte';
	import NotificationChannelFormDialog from '#lib/components/notification-channels/NotificationChannelFormDialog.svelte';
	import * as m from '#lib/paraglide/messages.js';

	const channels = notificationChannelsContext.set(new NotificationChannelsState());

	onMount(() => {
		void channels.load();
	});
</script>

<svelte:head>
	<title>{m.notificationChannelTable_heading()}</title>
</svelte:head>

<NotificationChannelTable />
<NotificationChannelFormDialog />
