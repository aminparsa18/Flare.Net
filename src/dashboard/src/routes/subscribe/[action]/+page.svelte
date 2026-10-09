<script lang="ts">
	// Public page (see +layout.svelte's PUBLIC_PREFIXES): the target of the signed links in status page
	// subscription emails (ADR-0162, ADR-0164). Loading it only describes the link - the change happens on the button,
	// so a mail scanner that fetches the URL can't confirm or unsubscribe anyone.
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { Checkbox } from '$lib/components/ui/checkbox';
	import {
		getSubscriptionInfo,
		getSubscriptionPreferences,
		redeemSubscription,
		saveSubscriptionPreferences,
		type SubscriptionAction,
		type SubscriptionInfo,
		type SubscriptionPreferences
	} from '$lib/status-pages-api';
	import * as m from '$lib/paraglide/messages';

	const action = $derived<SubscriptionAction | 'preferences' | null>(
		page.params.action === 'confirm' || page.params.action === 'unsubscribe' || page.params.action === 'preferences' ? page.params.action : null
	);
	const token = $derived(page.url.searchParams.get('token') ?? '');
	let info = $state<SubscriptionInfo | null>(null);
	let prefs = $state<SubscriptionPreferences | null>(null);
	let picked = $state<string[]>([]);
	let error = $state<string | null>(null);
	let busy = $state(false);
	let done = $state(false);

	const heading = $derived(
		action === 'unsubscribe' ? m.statusSubscribe_unsubscribeTitle() : action === 'preferences' ? m.statusSubscribe_preferencesTitle() : m.statusSubscribe_confirmTitle()
	);

	onMount(async () => {
		if (!action || !token) return;
		try {
			if (action === 'preferences') {
				prefs = await getSubscriptionPreferences(token);
				picked = prefs.selected;
				return;
			}
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
			if (action === 'preferences') {
				prefs = await saveSubscriptionPreferences(token, picked);
				picked = prefs.selected;
				done = true;
				return;
			}
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
	<title>{heading}</title>
</svelte:head>

<div class="flex min-h-screen items-center justify-center p-4">
	<Card.Root class="w-full max-w-sm">
		<Card.Header>
			<Card.Title>{heading}</Card.Title>
		</Card.Header>
		<Card.Content class="flex flex-col gap-3">
			{#if !action || !token}
				<Alert variant="destructive"><AlertDescription>{m.statusSubscribe_missingToken()}</AlertDescription></Alert>
			{:else}
				{#if error}
					<Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
				{/if}
				{#if prefs}
					<p class="text-sm">
						{done ? m.statusSubscribe_preferencesSaved({ email: prefs.email, page: prefs.pageTitle }) : m.statusSubscribe_preferencesPrompt({ email: prefs.email, page: prefs.pageTitle })}
					</p>
					<fieldset class="flex flex-col gap-1" disabled={busy}>
						{#each prefs.components as component (component.key)}
							<label class="flex items-center gap-2 text-sm">
								<Checkbox
									checked={picked.includes(component.key)}
									onCheckedChange={(v) => {
										done = false;
										picked = v === true ? [...picked, component.key] : picked.filter((k) => k !== component.key);
									}}
								/>
								{component.name}
							</label>
						{/each}
					</fieldset>
					<Button onclick={submit} disabled={busy}>{m.statusSubscribe_preferencesButton()}</Button>
				{:else if info}
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
