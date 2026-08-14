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
	import { Spinner } from '$lib/components/ui/spinner';
	import { authContext } from '$lib/auth/context';
	import { getBootstrapStatus, startEntraLogin, startOidcLogin, type BootstrapStatusResponse } from '$lib/auth-api';

	const auth = authContext.get();

	let username = $state('');
	let password = $state('');
	let confirmPassword = $state('');

	// Unlike every other method, reverse-proxy auth has no button/user action - identity
	// is already established by the time this page loads, so it's attempted
	// automatically (see auth.loginViaProxy()) as soon as the bootstrap-status fetch
	// below reports it's enabled. This flag gates the whole card while that attempt is
	// in flight, so the local-login form/other buttons don't flash on screen for a frame
	// before a silent sign-in either succeeds (this page navigates away) or fails (falls
	// through to whatever else is configured).
	let proxyAuthAttempted = $state(false);

	// Fetched independently of +layout.svelte's own bootstrap-status check (which only
	// runs when redirecting an unauthenticated visitor *to* /login) - a direct visit
	// here (bookmark, browser back) needs this too, and a second request on the
	// redirect path is a small, acceptable cost for that.
	let status = $state<BootstrapStatusResponse | null>(null);
	$effect(() => {
		getBootstrapStatus()
			.then(async (s) => {
				status = s;
				if (s.proxyAuthEnabled) {
					await auth.loginViaProxy();
					proxyAuthAttempted = true;
					if (auth.currentUser) {
						await goto('/');
					}
				}
			})
			.catch(() => {
				/* Sign-in still works with status left null - see the derived values below,
				   which all fail closed (no extra methods shown) rather than throwing. */
			});
	});

	const showProxyAuthLoading = $derived(status?.proxyAuthEnabled === true && !proxyAuthAttempted);
	// True once a reverse-proxy attempt has actually run and lost - as opposed to the
	// method just not being enabled at all - so the fallback UI below only shows this
	// specific error when there's something to explain.
	const proxyAuthFailed = $derived(status?.proxyAuthEnabled === true && proxyAuthAttempted && !auth.currentUser);

	const showBootstrap = $derived(status?.needsBootstrap === true && status?.localEnabled === true);
	const showLocalForm = $derived(status === null || status.localEnabled);
	const showEntraButton = $derived(status?.entraEnabled === true);
	const showOidcButton = $derived(status?.oidcEnabled === true);
	const showLdapOption = $derived(!showBootstrap && status?.ldapEnabled === true);

	// A segmented "Local / Active Directory" toggle only makes sense when both are
	// actually options - one form, one endpoint switched underneath it, rather than
	// stacking two near-identical-looking password forms on the page. Defaults to
	// "local" - matches this repo's own "keep a local break-glass path available"
	// reasoning from when AD was scoped (Planning.md).
	let loginMethod = $state<'local' | 'ldap'>('local');
	const usingLdap = $derived(showLdapOption && (loginMethod === 'ldap' || !showLocalForm));

	// EntraAuthEndpoints.HandleCompleteAsync/OidcAuthEndpoints.HandleCompleteAsync both
	// redirect here with this query param when the signed-in SSO account is disabled -
	// there's no in-flow way to show that inline the way a failed password POST can. The
	// query string alone doesn't say which provider triggered it, so the message below
	// stays provider-neutral rather than naming one.
	const ssoError = $derived(page.url.searchParams.get('error'));

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
			{#if showProxyAuthLoading}
				<div class="flex justify-center py-4"><Spinner /></div>
			{:else}
				{#if ssoError}
					<Alert variant="destructive" class="mb-3">
						<AlertDescription>
							{ssoError === 'account-disabled' ? 'That account is disabled. Contact an Admin.' : 'Sign-in failed.'}
						</AlertDescription>
					</Alert>
				{/if}
				{#if proxyAuthFailed && auth.error && !(showBootstrap || showLocalForm || showLdapOption)}
					<!-- Only shown when no form below is already going to render auth.error
					     inline (see that form's own {#if auth.error} block) - avoids
					     double-displaying the same message in a mixed proxy+local/LDAP setup. -->
					<Alert variant="destructive" class="mb-3">
						<AlertDescription>{auth.error}</AlertDescription>
					</Alert>
				{/if}
				{#if !showBootstrap && (showEntraButton || showOidcButton)}
					{#if showEntraButton}
						<Button variant="outline" class="mb-3 w-full" onclick={startEntraLogin}>Sign in with Microsoft</Button>
					{/if}
					{#if showOidcButton}
						<Button variant="outline" class="mb-3 w-full" onclick={startOidcLogin}>
							Sign in with {status?.oidcDisplayName || 'SSO'}
						</Button>
					{/if}
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
				{:else if !showEntraButton && !showOidcButton && !proxyAuthFailed}
					<p class="text-muted-foreground text-sm">No sign-in method is configured for this Flare instance.</p>
				{/if}
			{/if}
		</Card.Content>
	</Card.Root>
</div>
