<script lang="ts">
	// First shared app-shell nav - previously the logo lived inline in LogsToolbar since
	// the whole app was one route. Now that /alerts exists, the logo + page links are
	// hoisted here and mounted once in +layout.svelte so every route shares them; the
	// Logs toolbar keeps only its own filter controls.
	import { page } from '$app/state';
	import { Button, buttonVariants } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { cn } from '$lib/utils';
	import { Separator } from '$lib/components/ui/separator';
	import { authContext } from '$lib/auth/context';
	import { navLinks } from './nav-links';
	import TerminalModal from './TerminalModal.svelte';
	import SearchIcon from '@lucide/svelte/icons/search';
	import SunIcon from '@lucide/svelte/icons/sun';
	import MoonIcon from '@lucide/svelte/icons/moon';
	import { mode, toggleMode } from 'mode-watcher';

	// +layout.svelte renders AppNav once auth is off entirely (no currentUser then - see
	// its showChrome derived value) or once auth.currentUser is confirmed non-null - the
	// markup below branches on auth.authEnabled/currentUser itself rather than assuming
	// a user is always present.
	const auth = authContext.get();

	// Opens CommandPalette.svelte, mounted as this component's sibling in +layout.svelte -
	// lifted there (rather than owned privately by either component) since both this
	// button and the palette's own Cmd+K listener need to flip the same flag.
	let { commandPaletteOpen = $bindable(false) }: { commandPaletteOpen?: boolean } = $props();

	async function handleLogout() {
		// No goto() needed here - auth.currentUser flipping to null is itself what
		// +layout.svelte's route-guard $effect reacts to, which calls goto('/login')
		// on its own the moment this resolves.
		await auth.logout();
	}

	// /auth (the consolidated enable-auth/configure-methods/manage-users screen) is
	// Admin-only both server-side (UserEndpoints.cs/EntraSettingsEndpoints.cs/
	// AuthSettingsEndpoints.cs) and via +layout.svelte's own route guard - *except*
	// while auth is off entirely, when everyone has full access (opt-in auth, see
	// docs/auth.md) and needs a way to actually find where to turn it on. Shared with
	// CommandPalette.svelte's "Navigate" group via nav-links.ts - one source of truth.
	const links = $derived(navLinks(auth));

	// Exact match for every link except "/" (which would otherwise match every route,
	// since every pathname starts with "/") - first needed now that /traces/[traceId]
	// is this app's first nested route: visiting a trace's waterfall should still show
	// "Traces" as active, not nothing.
	function isActive(href: string, pathname: string): boolean {
		return href === '/' ? pathname === '/' : pathname === href || pathname.startsWith(`${href}/`);
	}
</script>

<nav class="bg-background flex shrink-0 items-center gap-3 border-b px-4 py-2">
	<img src="/logo.png" alt="Flare" class="h-8 w-auto shrink-0" />
	<Separator orientation="vertical" class="h-6" />
	<div class="flex items-center gap-1">
		{#each links as link (link.href)}
			<a
				href={link.href}
				class={cn(buttonVariants({ variant: isActive(link.href, page.url.pathname) ? 'secondary' : 'ghost', size: 'sm' }))}
			>
				{link.label}
			</a>
		{/each}
	</div>
	<Button
		variant="outline"
		size="sm"
		class="text-muted-foreground ml-2 w-64 justify-start"
		onclick={() => (commandPaletteOpen = true)}
	>
		<SearchIcon data-icon="inline-start" />
		Search or run a command...
		<kbd class="bg-muted text-muted-foreground ml-auto rounded border px-1.5 py-0.5 font-mono text-[0.625rem]">
			⌘K
		</kbd>
	</Button>
	<TerminalModal />
	<!-- mode.current is undefined during SSR (mode-watcher's isBrowser guard) - the icon
	     briefly defaults to Moon in that window, corrected the instant the client hydrates.
	     Harmless: the anti-FOUC script in +layout.svelte already set the *page's* actual
	     theme correctly before paint, this only affects which icon this one button shows
	     for a frame. Moved here from LogsToolbar - app-wide, not a Logs-page-only control. -->
	<Button variant="outline" size="icon-sm" onclick={toggleMode} title="Toggle dark/light theme">
		{#if mode.current === 'light'}
			<SunIcon />
		{:else}
			<MoonIcon />
		{/if}
	</Button>
	<div class="ml-auto flex items-center gap-2">
		{#if auth.authEnabled}
			<span class="text-muted-foreground text-xs">{auth.currentUser?.username}</span>
			<Badge variant="outline">{auth.currentUser?.role}</Badge>
			<Button variant="ghost" size="sm" onclick={handleLogout} disabled={auth.loading}>Log out</Button>
		{:else}
			<a href="/auth" class={cn(buttonVariants({ variant: 'outline', size: 'sm' }))}>Auth is off</a>
		{/if}
	</div>
</nav>
