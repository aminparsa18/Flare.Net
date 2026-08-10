<script lang="ts">
	// +layout.svelte's route guard is what actually sends an unauthenticated visitor
	// here (or to /setup, if no admin exists yet) - this page itself doesn't check
	// anything on mount, it just submits credentials.
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Separator } from '$lib/components/ui/separator';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { authContext } from '$lib/auth/context';
	import { getBootstrapStatus, startEntraLogin } from '$lib/auth-api';

	const auth = authContext.get();

	let username = $state('');
	let password = $state('');

	// Fetched independently of +layout.svelte's own bootstrap-status check (which only
	// runs when redirecting an unauthenticated visitor *to* /login) - a direct visit
	// here (bookmark, browser back) needs this too, and a second request on the
	// redirect path is a small, acceptable cost for that.
	let entraEnabled = $state(false);
	$effect(() => {
		getBootstrapStatus()
			.then((status) => (entraEnabled = status.entraEnabled))
			.catch(() => {
				/* Sign in with Microsoft just doesn't render - the password form (and
				   +layout.svelte's own error handling for an unreachable API) still work. */
			});
	});

	// EntraAuthEndpoints.HandleCompleteAsync redirects here with this query param when
	// the signed-in Entra account is disabled - there's no in-flow way to show that
	// inline the way a failed password POST can.
	const entraError = $derived(page.url.searchParams.get('error'));

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
			{#if entraError}
				<Alert variant="destructive" class="mb-3">
					<AlertDescription>
						{entraError === 'account-disabled'
							? 'That Microsoft account is disabled. Contact an Admin.'
							: 'Sign-in with Microsoft failed.'}
					</AlertDescription>
				</Alert>
			{/if}
			{#if entraEnabled}
				<Button variant="outline" class="mb-3 w-full" onclick={startEntraLogin}>Sign in with Microsoft</Button>
				<div class="mb-3 flex items-center gap-2">
					<Separator class="flex-1" />
					<span class="text-muted-foreground text-xs">or</span>
					<Separator class="flex-1" />
				</div>
			{/if}
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
