<script lang="ts">
	// Message-queue producers/consumers from spans' OTel `messaging.*` attributes, plus
	// Kafka consumer lag where a collector exports it - see
	// docs-internal/adr/0056-messaging-queue-monitoring.md.
	import { onMount, onDestroy } from 'svelte';
	import { MessagingState } from '$lib/messaging/state.svelte';
	import { messagingContext } from '$lib/messaging/context';
	import MessagingToolbar from '$lib/components/messaging/MessagingToolbar.svelte';
	import MessagingTable from '$lib/components/messaging/MessagingTable.svelte';
	import MessagingDetailSheet from '$lib/components/messaging/MessagingDetailSheet.svelte';
	import * as m from '$lib/paraglide/messages';

	const messaging = messagingContext.set(new MessagingState());

	onMount(() => {
		void messaging.load();
	});

	onDestroy(() => {
		messaging.dispose();
	});
</script>

<svelte:head>
	<title>{m.messagingPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<MessagingToolbar />
	<MessagingTable />
	<MessagingDetailSheet />
</div>
