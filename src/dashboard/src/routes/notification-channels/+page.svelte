<script lang="ts">
	import { onMount } from 'svelte';
	import { NotificationChannelsState } from '$lib/notification-channels/state.svelte';
	import { notificationChannelsContext } from '$lib/notification-channels/context';
	import NotificationChannelTable from '$lib/components/notification-channels/NotificationChannelTable.svelte';
	import NotificationChannelFormDialog from '$lib/components/notification-channels/NotificationChannelFormDialog.svelte';
	import * as m from '$lib/paraglide/messages';

	const channels = notificationChannelsContext.set(new NotificationChannelsState());

	onMount(() => {
		void channels.load();
	});
</script>

<svelte:head>
	<title>{m.notificationChannelsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col">
	<NotificationChannelTable />
</div>
<NotificationChannelFormDialog />
