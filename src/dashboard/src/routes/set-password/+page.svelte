<script lang="ts">
	// Public page (see +layout.svelte's onPublicRoute): redeems an invite / admin-generated
	// reset token from `?token=` and sets the account's password.
	import { page } from '$app/state';
	import { withBase } from '$lib/paths';
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { setPasswordWithToken } from '$lib/users-api';
	import * as m from '$lib/paraglide/messages';

	const token = $derived(page.url.searchParams.get('token') ?? '');
	let password = $state('');
	let confirm = $state('');
	let error = $state<string | null>(null);
	let busy = $state(false);
	let done = $state(false);

	async function submit(e: SubmitEvent) {
		e.preventDefault();
		error = null;
		if (password.length < 8) return void (error = m.setPassword_tooShort());
		if (password !== confirm) return void (error = m.setPassword_mismatch());
		busy = true;
		try {
			await setPasswordWithToken(token, password);
			done = true;
		} catch (err) {
			error = err instanceof Error && err.message === 'invalid' ? m.setPassword_invalid() : String(err);
		} finally {
			busy = false;
		}
	}
</script>

<div class="flex min-h-screen items-center justify-center p-4">
	<Card.Root class="w-full max-w-sm">
		<Card.Header>
			<Card.Title>{m.setPassword_title()}</Card.Title>
			<Card.Description>{m.setPassword_description()}</Card.Description>
		</Card.Header>
		<Card.Content>
			{#if !token}
				<Alert variant="destructive"><AlertDescription>{m.setPassword_missingToken()}</AlertDescription></Alert>
			{:else if done}
				<p class="mb-4 text-sm">{m.setPassword_success()}</p>
				<Button href={withBase('/login')}>{m.setPassword_signIn()}</Button>
			{:else}
				<form class="flex flex-col gap-3" onsubmit={submit}>
					{#if error}
						<Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
					{/if}
					<Input type="password" autocomplete="new-password" placeholder={m.setPassword_password()} bind:value={password} />
					<Input type="password" autocomplete="new-password" placeholder={m.setPassword_confirm()} bind:value={confirm} />
					<Button type="submit" disabled={busy}>{m.setPassword_submit()}</Button>
				</form>
			{/if}
		</Card.Content>
	</Card.Root>
</div>
