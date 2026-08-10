<script lang="ts">
	import './layout.css';
	import { ModeWatcher } from 'mode-watcher';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import AppNav from '$lib/components/nav/AppNav.svelte';
	import { Spinner } from '$lib/components/ui/spinner';
	import { Alert, AlertTitle, AlertDescription } from '$lib/components/ui/alert';
	import { Button } from '$lib/components/ui/button';
	import { AuthState } from '$lib/auth/state.svelte';
	import { authContext } from '$lib/auth/context';
	import { getBootstrapStatus } from '$lib/auth-api';

	const { children } = $props();

	const auth = new AuthState();
	authContext.set(auth);

	const AUTH_ROUTES = ['/login', '/setup'];

	// $effect bodies never run during SSR (Svelte 5's server renderer only evaluates
	// template/derived state, not effects) - this only ever fires client-side, once, on
	// hydration. The SSR'd shell below always renders the "checking session" spinner
	// (auth.initializing defaults to true on a freshly-constructed AuthState), so no
	// route's actual content is ever painted before the client has confirmed a session.
	$effect(() => {
		auth.initialize();
	});

	const onAuthRoute = $derived(AUTH_ROUTES.includes(page.url.pathname));

	// /users is the first Admin-only route (UserEndpoints.cs enforces this server-side
	// regardless - this is a UX nicety, not the actual access control) - same reasoning
	// as EntraAuthEndpoints.HandleLoginAsync's returnUrl validation being "defense in
	// depth" on top of server checks that already exist.
	const ADMIN_ONLY_ROUTES = ['/users', '/security'];
	const onAdminOnlyRoute = $derived(ADMIN_ONLY_ROUTES.includes(page.url.pathname));

	// Route guard: bounces between the app and /login|/setup based on session state.
	// Doesn't run (and doesn't need to) until auth.initializing flips false - see above.
	$effect(() => {
		if (auth.initializing) return;

		if (auth.currentUser) {
			if (onAuthRoute) void goto('/');
			else if (onAdminOnlyRoute && auth.currentUser.role !== 'Admin') void goto('/');
			return;
		}

		if (!onAuthRoute) {
			void redirectUnauthenticated();
		}
	});

	// Set only when redirectUnauthenticated's own fetch fails (e.g. a CORS
	// misconfiguration, or Flare.Api simply unreachable) - without this, that failure
	// would otherwise be a silently-swallowed promise rejection: goto() never runs,
	// readyToRenderChildren never becomes true, and the spinner below spins forever with
	// no indication anything is wrong. Found exactly this way, live, against a real
	// Aspire-orchestrated deployment - see git history for the incident this fixed.
	let redirectError = $state<string | null>(null);

	async function redirectUnauthenticated(): Promise<void> {
		redirectError = null;
		try {
			// Re-checked on every redirect (not cached) - an admin could have been
			// created in another tab since the last check, and this is the one place
			// that matters: whichever of /login or /setup we send an unauthenticated
			// visitor to.
			const status = await getBootstrapStatus();
			await goto(status.needsBootstrap ? '/setup' : '/login');
		} catch (err) {
			redirectError = err instanceof Error ? err.message : String(err);
		}
	}

	// True once it's actually safe to render whatever route the user is on - either
	// they're authenticated (and not a non-Admin on an Admin-only route), or they're
	// already on /login|/setup (which render regardless of auth state, by definition).
	// False in between means a redirect is pending (see redirectUnauthenticated above) -
	// render the spinner, not the route's real content, so a protected page (including
	// an Admin-only one) never flashes before the bounce completes.
	const readyToRenderChildren = $derived(
		!auth.initializing &&
			(onAuthRoute || (auth.currentUser !== null && (!onAdminOnlyRoute || auth.currentUser.role === 'Admin')))
	);

	// Nav makes no sense on the login/setup screens themselves (its links would just
	// bounce back through the guard above) or while nothing's confirmed yet.
	const showChrome = $derived(auth.currentUser !== null && !onAuthRoute);
</script>

<!-- Handles both the dark/light class on <html> and, crucially, injects an inline
     anti-FOUC script into <head> (see mode-watcher's ModeWatcherFull) that runs
     synchronously before first paint - this app is SSR'd (adapter-node, no ssr=false
     anywhere), so without that script the page would flash the wrong theme on every
     load until Svelte hydrates. Defaults: system preference until the user picks
     explicitly (then persisted to localStorage), darkClassNames: ["dark"] - already
     exactly what layout.css's .dark selector expects, so no config needed here. -->
<ModeWatcher />

{#if readyToRenderChildren}
	<!-- Shared across every route (Logs, Alerts, ...) - a route's own root div is
	     responsible for its own scroll/height handling below this, same as the Logs
	     page's `flex h-screen flex-col` already did before this nav existed. -->
	<div class="flex h-screen flex-col">
		{#if showChrome}
			<AppNav />
		{/if}
		<div class="min-h-0 flex-1">
			{@render children()}
		</div>
	</div>
{:else if redirectError}
	<div class="flex h-screen items-center justify-center p-4">
		<Alert variant="destructive" class="max-w-md">
			<AlertTitle>Can't reach Flare.Api</AlertTitle>
			<AlertDescription class="flex flex-col gap-2">
				<span>{redirectError}</span>
				<span>
					Check that this dashboard's origin is listed in Flare.Api's
					<code>Cors:AllowedOrigins</code>, and that <code>PUBLIC_API_URL</code>
					points at a reachable address.
				</span>
				<Button size="sm" variant="outline" onclick={() => void redirectUnauthenticated()} class="self-start">
					Retry
				</Button>
			</AlertDescription>
		</Alert>
	</div>
{:else}
	<div class="flex h-screen items-center justify-center">
		<Spinner class="size-6" />
	</div>
{/if}
