<script lang="ts">
	// +layout.svelte's route guard is what actually sends an unauthenticated visitor
	// here (or to /setup, if no admin exists yet) - this page itself doesn't check
	// anything on mount, it just submits credentials.
	import { goto } from '$app/navigation';
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { authContext } from '$lib/auth/context';

	const auth = authContext.get();

	let username = $state('');
	let password = $state('');

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		await auth.login(username, password);
		if (auth.currentUser) {
			await goto('/');
		}
	}
</script>

<svelte:head>
	<title>Sign in - Flare</title>
</svelte:head>

<div class="flex h-full items-center justify-center p-4">
	<Card.Root class="w-full max-w-sm">
		<Card.Header>
			<Card.Title>Sign in to Flare</Card.Title>
			<Card.Description>Enter your username and password.</Card.Description>
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
					<Input id="password" type="password" bind:value={password} autocomplete="current-password" required />
				</div>
				<Button type="submit" disabled={auth.loading}>{auth.loading ? 'Signing in…' : 'Sign in'}</Button>
			</form>
		</Card.Content>
	</Card.Root>
</div>
