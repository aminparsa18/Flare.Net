<script lang="ts">
	// +layout.svelte's route guard is what actually sends an unauthenticated visitor
	// here whenever auth is on and there's no session - this page itself decides,
	// from GET /api/auth/bootstrap/status, whether to show the first-run "create admin"
	// form (folded in here rather than a separate /setup route now that reaching this
	// page at all is conditional on opt-in auth - see docs/auth.md) or the normal
	// sign-in form(s).
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import * as Card from '$lib/components/ui/card';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Separator } from '$lib/components/ui/separator';
	import { Alert, AlertDescription } from '$lib/components/ui/alert';
	import { authContext } from '$lib/auth/context';
	import { getBootstrapStatus, startEntraLogin, type BootstrapStatusResponse } from '$lib/auth-api';

	const auth = authContext.get();

	let username = $state('');
	let password = $state('');
	let confirmPassword = $state('');

	// Fetched independently of +layout.svelte's own bootstrap-status check (which only
	// runs when redirecting an unauthenticated visitor *to* /login) - a direct visit
	// here (bookmark, browser back) needs this too, and a second request on the
	// redirect path is a small, acceptable cost for that.
	let status = $state<BootstrapStatusResponse | null>(null);
	$effect(() => {
		getBootstrapStatus()
			.then((s) => (status = s))
			.catch(() => {
				/* Sign-in still works with status left null - see the derived values below,
				   which all fail closed (no extra methods shown) rather than throwing. */
			});
	});

	const showBootstrap = $derived(status?.needsBootstrap === true && status?.localEnabled === true);
	const showLocalForm = $derived(status === null || status.localEnabled);
	const showEntraButton = $derived(status?.entraEnabled === true);
	const showLdapOption = $derived(!showBootstrap && status?.ldapEnabled === true);

	// A segmented "Local / Active Directory" toggle only makes sense when both are
	// actually options - one form, one endpoint switched underneath it, rather than
	// stacking two near-identical-looking password forms on the page. Defaults to
	// "local" - matches this repo's own "keep a local break-glass path available"
	// reasoning from when AD was scoped (Planning.md).
	let loginMethod = $state<'local' | 'ldap'>('local');
	const usingLdap = $derived(showLdapOption && (loginMethod === 'ldap' || !showLocalForm));

	// EntraAuthEndpoints.HandleCompleteAsync redirects here with this query param when
	// the signed-in Entra account is disabled - there's no in-flow way to show that
	// inline the way a failed password POST can.
	const entraError = $derived(page.url.searchParams.get('error'));

	// Client-side only - AuthEndpoints.HandleBootstrapAsync enforces the real
	// 8-character minimum server-side; this is just UX, not the source of truth.
	const passwordsMatch = $derived(confirmPassword.length === 0 || password === confirmPassword);
	const canSubmitBootstrap = $derived(username.trim().length > 0 && password.length >= 8 && password === confirmPassword);

	async function handleSubmit(event: SubmitEvent) {
		event.preventDefault();
		if (showBootstrap) {
			if (!canSubmitBootstrap) return;
			await auth.bootstrap(username, password);
		} else if (usingLdap) {
			await auth.loginLdap(username, password);
		} else {
			await auth.login(username, password);
		}
		if (auth.currentUser) {
			await goto('/');
		}
	}
</script>

<svelte:head>
	<title>{showBootstrap ? 'Set up Flare' : 'Sign in - Flare'}</title>
</svelte:head>

<div class="flex h-full items-center justify-center p-4">
	<Card.Root class="w-full max-w-sm">
		<Card.Header>
			{#if showBootstrap}
				<Card.Title>Create the admin account</Card.Title>
				<Card.Description>One-time setup - no admin account exists yet for this Flare instance.</Card.Description>
			{:else}
				<Card.Title>Sign in to Flare</Card.Title>
				<Card.Description>Enter your username and password.</Card.Description>
			{/if}
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
			{#if !showBootstrap && showEntraButton}
				<Button variant="outline" class="mb-3 w-full" onclick={startEntraLogin}>Sign in with Microsoft</Button>
				{#if showLocalForm || showLdapOption}
					<div class="mb-3 flex items-center gap-2">
						<Separator class="flex-1" />
						<span class="text-muted-foreground text-xs">or</span>
						<Separator class="flex-1" />
					</div>
				{/if}
			{/if}
			{#if showBootstrap || showLocalForm || showLdapOption}
				{#if !showBootstrap && showLocalForm && showLdapOption}
					<!-- One form, one endpoint switched underneath it, rather than stacking two
					     near-identical-looking password forms on the page. -->
					<div class="mb-3 grid grid-cols-2 gap-1 rounded-md border p-1">
						<Button
							type="button"
							size="sm"
							variant={loginMethod === 'local' ? 'secondary' : 'ghost'}
							onclick={() => (loginMethod = 'local')}
						>
							Local
						</Button>
						<Button
							type="button"
							size="sm"
							variant={loginMethod === 'ldap' ? 'secondary' : 'ghost'}
							onclick={() => (loginMethod = 'ldap')}
						>
							Active Directory
						</Button>
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
						<Input
							id="password"
							type="password"
							bind:value={password}
							autocomplete={showBootstrap ? 'new-password' : 'current-password'}
							minlength={showBootstrap ? 8 : undefined}
							required
						/>
						{#if showBootstrap}
							<p class="text-muted-foreground text-xs">At least 8 characters.</p>
						{/if}
					</div>
					{#if showBootstrap}
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
					{/if}
					<Button type="submit" disabled={auth.loading || (showBootstrap && !canSubmitBootstrap)}>
						{#if auth.loading}
							{showBootstrap ? 'Creating…' : 'Signing in…'}
						{:else}
							{showBootstrap ? 'Create admin account' : 'Sign in'}
						{/if}
					</Button>
				</form>
			{:else if !showEntraButton}
				<p class="text-muted-foreground text-sm">No sign-in method is configured for this Flare instance.</p>
			{/if}
		</Card.Content>
	</Card.Root>
</div>
