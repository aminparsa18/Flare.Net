<script lang="ts">
	import './layout.css';
	import { ModeWatcher } from 'mode-watcher';
	import { goto } from '$app/navigation';
	import { page } from '$app/state';
	import AppNav from '$lib/components/nav/AppNav.svelte';
	import { Spinner } from '$lib/components/ui/spinner';
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

	// Route guard: bounces between the app and /login|/setup based on session state.
	// Doesn't run (and doesn't need to) until auth.initializing flips false - see above.
	$effect(() => {
		if (auth.initializing) return;

		if (auth.currentUser) {
			if (onAuthRoute) void goto('/');
			return;
		}

		if (!onAuthRoute) {
			void redirectUnauthenticated();
		}
	});

	async function redirectUnauthenticated(): Promise<void> {
		// Re-checked on every redirect (not cached) - an admin could have been created in
		// another tab since the last check, and this is the one place that matters:
		// whichever of /login or /setup we send an unauthenticated visitor to.
		const status = await getBootstrapStatus();
		await goto(status.needsBootstrap ? '/setup' : '/login');
	}

	// True once it's actually safe to render whatever route the user is on - either
	// they're authenticated, or they're already on /login|/setup (which render
	// regardless of auth state, by definition). False in between means a redirect is
	// pending (see redirectUnauthenticated above) - render the spinner, not the route's
	// real content, so a protected page never flashes before the bounce completes.
	const readyToRenderChildren = $derived(!auth.initializing && (auth.currentUser !== null || onAuthRoute));

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
{:else}
	<div class="flex h-screen items-center justify-center">
		<Spinner class="size-6" />
	</div>
{/if}
