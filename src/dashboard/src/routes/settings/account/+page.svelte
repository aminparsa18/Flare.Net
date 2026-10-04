<script lang="ts">
	// Account & security: password change for local accounts (POST /api/auth/password; SSO
	// accounts have no Flare-managed password, so that card is hidden for them), active
	// sessions, and a data export.
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import SessionsCard from '$lib/components/settings/SessionsCard.svelte';
	import DataExportCard from '$lib/components/settings/DataExportCard.svelte';
	import { authContext } from '$lib/auth/context';
	import { changeOwnPassword } from '$lib/users-api';
	import * as m from '$lib/paraglide/messages';

	const auth = authContext.get();
	const isLocal = $derived(auth.currentUser?.authProvider === 'Local');

	let current = $state('');
	let next = $state('');
	let confirm = $state('');
	let error = $state<string | null>(null);
	let success = $state(false);
	let busy = $state(false);

	async function submit(e: SubmitEvent) {
		e.preventDefault();
		error = null;
		success = false;
		if (next.length < 8) return void (error = m.settingsAccount_tooShort());
		if (next !== confirm) return void (error = m.settingsAccount_mismatch());
		busy = true;
		try {
			await changeOwnPassword(current, next);
			current = next = confirm = '';
			success = true;
		} catch (err) {
			error = err instanceof Error && err.message === 'invalid' ? m.settingsAccount_wrongCurrent() : String(err);
		} finally {
			busy = false;
		}
	}
</script>

<svelte:head>
	<title>{m.settingsAccount_title()}</title>
</svelte:head>

<div class="flex flex-col gap-6">
{#if isLocal}
<Card.Root>
	<Card.Header>
		<Card.Title>{m.settingsAccount_heading()}</Card.Title>
		<Card.Description>{m.settingsAccount_description()}</Card.Description>
	</Card.Header>
	<Card.Content>
		<form class="flex max-w-sm flex-col gap-3" onsubmit={submit}>
			{#if error}
				<Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
			{/if}
			{#if success}
				<Alert><AlertDescription>{m.settingsAccount_success()}</AlertDescription></Alert>
			{/if}
			<Input type="password" autocomplete="current-password" placeholder={m.settingsAccount_current()} bind:value={current} />
			<Input type="password" autocomplete="new-password" placeholder={m.settingsAccount_new()} bind:value={next} />
			<Input type="password" autocomplete="new-password" placeholder={m.settingsAccount_confirm()} bind:value={confirm} />
			<Button type="submit" disabled={busy || !current || !next}>{m.settingsAccount_submit()}</Button>
		</form>
	</Card.Content>
</Card.Root>
{/if}

<SessionsCard />
<DataExportCard />
</div>
