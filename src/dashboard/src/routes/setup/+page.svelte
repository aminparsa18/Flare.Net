<script lang="ts">
	// First-run only: +layout.svelte's route guard sends an unauthenticated visitor here
	// (instead of /login) when GET /api/auth/bootstrap/status reports no admin account
	// exists yet for this Flare instance. POST /api/auth/bootstrap itself 409s if one
	// was created in the meantime (e.g. a second browser tab) - auth.error surfaces that.
	import { goto } from '$app/navigation';
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { authContext } from '$lib/auth/context';

	const auth = authContext.get();

	let username = $state('');
	let password = $state('');
	let confirmPassword = $state('');

	// Client-side only - AuthEndpoints.HandleBootstrapAsync enforces the real 8-character
	// minimum server-side (see src/Flare.Api/Endpoints/AuthEndpoints.cs); this is just
	// UX, not the source of truth for the rule.
	const passwordsMatch = $derived(confirmPassword.length === 0 || password === confirmPassword);
	const canSubmit = $derived(username.trim().length > 0 && password.length >= 8 && password === confirmPassword);

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		if (!canSubmit) return;
		await auth.bootstrap(username, password);
		if (auth.currentUser) {
			await goto('/');
		}
	}
</script>

<svelte:head>
	<title>Set up Flare</title>
</svelte:head>

<div class="flex h-full items-center justify-center p-4">
	<Card.Root class="w-full max-w-sm">
		<Card.Header>
			<Card.Title>Create the admin account</Card.Title>
			<Card.Description>One-time setup - no admin account exists yet for this Flare instance.</Card.Description>
		</Card.Header>
		<Card.Content>
			<form class="flex flex-col gap-3" onsubmit={handleSubmit}>
				{#if auth.error}
					<Alert variant="destructive">
						<AlertDescription>{auth.error}</AlertDescription>
					</Alert>
				{/if}
				<div class="flex flex-col gap-1">
					<label for="username" class="text-xs font-medium">Username</label>
					<Input id="username" bind:value={username} autocomplete="username" autofocus required />
				</div>
				<div class="flex flex-col gap-1">
					<label for="password" class="text-xs font-medium">Password</label>
					<Input id="password" type="password" bind:value={password} autocomplete="new-password" minlength={8} required />
					<p class="text-muted-foreground text-xs">At least 8 characters.</p>
				</div>
				<div class="flex flex-col gap-1">
					<label for="confirm-password" class="text-xs font-medium">Confirm password</label>
					<Input
						id="confirm-password"
						type="password"
						bind:value={confirmPassword}
						autocomplete="new-password"
						required
						aria-invalid={!passwordsMatch}
					/>
					{#if !passwordsMatch}
						<p class="text-destructive text-xs">Passwords don't match.</p>
					{/if}
				</div>
				<Button type="submit" disabled={auth.loading || !canSubmit}>{auth.loading ? 'Creating…' : 'Create admin account'}</Button>
			</form>
		</Card.Content>
	</Card.Root>
</div>
