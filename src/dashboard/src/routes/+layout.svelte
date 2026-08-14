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

	// /setup no longer exists as its own route - the first-run "create admin" form is
	// now folded into /login itself (see that page), reached only when auth is actually
	// on and there's no session, per opt-in auth (docs/auth.md).
	const AUTH_ROUTES = ['/login'];

	// $effect bodies never run during SSR (Svelte 5's server renderer only evaluates
	// template/derived state, not effects) - this only ever fires client-side, once, on
	// hydration. The SSR'd shell below always renders the "checking session" spinner
	// (auth.initializing defaults to true on a freshly-constructed AuthState), so no
	// route's actual content is ever painted before the client has confirmed a session.
	$effect(() => {
		auth.initialize();
	});

	const onAuthRoute = $derived(AUTH_ROUTES.includes(page.url.pathname));

	// /auth (the consolidated enable-auth/configure-methods/manage-users screen) is the
	// one Admin-only route (server-side enforcement lives in the endpoints it calls -
	// UserEndpoints.cs/EntraSettingsEndpoints.cs/AuthSettingsEndpoints.cs - this is a UX
	// nicety, not the actual access control) - same reasoning as
	// EntraAuthEndpoints.HandleLoginAsync's returnUrl validation being "defense in
	// depth" on top of server checks that already exist. Not gated at all while auth is
	// off (see below) - anyone needs to be able to reach it to turn auth on.
	const ADMIN_ONLY_ROUTES = ['/auth'];
	const onAdminOnlyRoute = $derived(ADMIN_ONLY_ROUTES.includes(page.url.pathname));

	// Route guard: bounces between the app and /login based on session state - but only
	// when auth.authEnabled is true. Opt-in auth (docs/auth.md): a fresh Flare instance
	// has no login requirement at all, so when it's off, every route (including
	// admin-only ones - nobody has a role yet) renders freely; /login itself is
	// meaningless in that state, so visiting it directly still bounces to "/".
	// Doesn't run (and doesn't need to) until auth.initializing flips false - see above.
	$effect(() => {
		if (auth.initializing) return;

		if (!auth.authEnabled) {
			if (onAuthRoute) void goto('/');
			return;
		}

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
			// The fetch itself is what matters here now (a live connectivity/CORS probe
			// before letting the app render at all) - /login vs "needs bootstrap" is
			// decided by /login's own fresh fetch of this same endpoint on mount, not
			// here, now that both live on the same route.
			await getBootstrapStatus();
			await goto('/login');
		} catch (err) {
			redirectError = err instanceof Error ? err.message : String(err);
		}
	}

	// True once it's actually safe to render whatever route the user is on. Three ways
	// to get there: auth is off entirely and we're not sitting on the now-meaningless
	// /login (mid-redirect-away); auth is on and we're on /login itself; or auth is on,
	// we're authenticated, and not a non-Admin on an Admin-only route. False in between
	// means a redirect is pending (see redirectUnauthenticated above) - render the
	// spinner, not the route's real content, so a protected page never flashes before
	// the bounce completes.
	const readyToRenderChildren = $derived(
		!auth.initializing &&
			(!auth.authEnabled
				? !onAuthRoute
				: onAuthRoute || (auth.currentUser !== null && (!onAdminOnlyRoute || auth.currentUser.role === 'Admin')))
	);

	// Nav makes sense whenever there's real content to navigate between: either auth is
	// off (everyone gets full access, including finding their way to turn it on) or
	// someone's actually signed in - either way, never on the /login screen itself (its
	// links would just bounce back through the guard above).
	const showChrome = $derived(!onAuthRoute && (!auth.authEnabled || auth.currentUser !== null));
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
