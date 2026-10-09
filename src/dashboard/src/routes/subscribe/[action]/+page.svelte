<script lang="ts">
	// Public page (see +layout.svelte's PUBLIC_PREFIXES): the target of the signed links in status page
	// subscription emails (ADR-0162). Loading it only describes the link - the change happens on the button,
	// so a mail scanner that fetches the URL can't confirm or unsubscribe anyone.
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { getSubscriptionInfo, redeemSubscription, type SubscriptionAction, type SubscriptionInfo } from '$lib/status-pages-api';
	import * as m from '$lib/paraglide/messages';

	const action = $derived<SubscriptionAction | null>(page.params.action === 'confirm' || page.params.action === 'unsubscribe' ? page.params.action : null);
	const token = $derived(page.url.searchParams.get('token') ?? '');
	let info = $state<SubscriptionInfo | null>(null);
	let error = $state<string | null>(null);
	let busy = $state(false);
	let done = $state(false);

	onMount(async () => {
		if (!action || !token) return;
		try {
			info = await getSubscriptionInfo(action, token);
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		}
	});

	async function submit() {
		if (!action) return;
		busy = true;
		error = null;
		try {
			info = await redeemSubscription(action, token);
			done = true;
		} catch (err) {
			error = err instanceof Error ? err.message : String(err);
		} finally {
			busy = false;
		}
	}
</script>

<svelte:head>
	<title>{action === 'unsubscribe' ? m.statusSubscribe_unsubscribeTitle() : m.statusSubscribe_confirmTitle()}</title>
</svelte:head>

<div class="flex min-h-screen items-center justify-center p-4">
	<Card.Root class="w-full max-w-sm">
		<Card.Header>
			<Card.Title>{action === 'unsubscribe' ? m.statusSubscribe_unsubscribeTitle() : m.statusSubscribe_confirmTitle()}</Card.Title>
		</Card.Header>
		<Card.Content class="flex flex-col gap-3">
			{#if !action || !token}
				<Alert variant="destructive"><AlertDescription>{m.statusSubscribe_missingToken()}</AlertDescription></Alert>
			{:else}
				{#if error}
					<Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
				{/if}
				{#if info}
					<p class="text-sm">
						{#if done}
							{action === 'unsubscribe' ? m.statusSubscribe_unsubscribed({ email: info.email, page: info.pageTitle }) : m.statusSubscribe_confirmed({ email: info.email, page: info.pageTitle })}
						{:else}
							{action === 'unsubscribe' ? m.statusSubscribe_unsubscribePrompt({ email: info.email, page: info.pageTitle }) : m.statusSubscribe_confirmPrompt({ email: info.email, page: info.pageTitle })}
						{/if}
					</p>
					{#if !done}
						<Button onclick={submit} disabled={busy}>
							{action === 'unsubscribe' ? m.statusSubscribe_unsubscribeButton() : m.statusSubscribe_confirmButton()}
						</Button>
					{/if}
				{/if}
			{/if}
		</Card.Content>
	</Card.Root>
</div>
