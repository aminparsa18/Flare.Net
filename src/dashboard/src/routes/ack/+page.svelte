<script lang="ts">
	// Public page (see +layout.svelte's PUBLIC_ROUTES): the target of the signed link a firing alert's
	// notification carries as {{ack_url}} (ADR-0127). Loading it only describes the alert - the
	// acknowledgement happens on the button, so a mail scanner or chat unfurler that fetches the URL
	// can't acknowledge anything.
	import { onMount } from 'svelte';
	import { page } from '$app/state';
	import { withBase } from '$lib/paths';
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { AckLinkError, getAckLink, redeemAckLink, type AckLinkInfo } from '$lib/alerts-api';
	import * as m from '$lib/paraglide/messages';

	const token = $derived(page.url.searchParams.get('token') ?? '');
	let info = $state<AckLinkInfo | null>(null);
	let error = $state<string | null>(null);
	let busy = $state(false);
	let done = $state(false);

	function describe(err: unknown): string {
		if (err instanceof AckLinkError) {
			if (err.kind === 'invalid') return m.ackLink_invalid();
			if (err.kind === 'notFiring') return m.ackLink_notFiring();
		}
		return String(err);
	}

	onMount(async () => {
		if (!token) return;
		try {
			info = await getAckLink(token);
		} catch (err) {
			error = describe(err);
		}
	});

	async function confirm() {
		busy = true;
		error = null;
		try {
			info = await redeemAckLink(token);
			done = true;
		} catch (err) {
			error = describe(err);
		} finally {
			busy = false;
		}
	}

	// A snooze that has already run out no longer holds anything back, so the button comes back.
	const alreadyHandled = $derived(
		info?.ack && (info.ack.kind === 'Ack' || (info.ack.kind === 'Snooze' && new Date(info.ack.snoozedUntil ?? 0) > new Date())) ? info.ack : null
	);
</script>

<div class="flex min-h-screen items-center justify-center p-4">
	<Card.Root class="w-full max-w-sm">
		<Card.Header>
			<Card.Title>{m.ackLink_title()}</Card.Title>
			<Card.Description>{m.ackLink_description()}</Card.Description>
		</Card.Header>
		<Card.Content class="flex flex-col gap-3">
			{#if !token}
				<Alert variant="destructive"><AlertDescription>{m.ackLink_missingToken()}</AlertDescription></Alert>
			{:else}
				{#if error}
					<Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
				{/if}
				{#if info}
					<p class="text-sm font-medium">{info.ruleName}</p>
					{#if done}
						<p class="text-sm">{m.ackLink_done()}</p>
					{:else if alreadyHandled}
						<p class="text-sm">
							{#if alreadyHandled.kind === 'Snooze'}
								{m.ackLink_snoozed()}
							{:else if alreadyHandled.ackedBy}
								{m.ackLink_alreadyBy({ user: alreadyHandled.ackedBy })}
							{:else}
								{m.ackLink_alreadyAnon()}
							{/if}
						</p>
					{:else}
						<Button onclick={confirm} disabled={busy}>{m.ackLink_confirm()}</Button>
					{/if}
					<Button variant="outline" href={withBase('/alerts')}>{m.ackLink_openAlerts()}</Button>
				{/if}
			{/if}
		</Card.Content>
	</Card.Root>
</div>
