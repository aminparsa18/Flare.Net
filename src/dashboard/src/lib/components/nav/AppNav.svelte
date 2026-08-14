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

	// +layout.svelte renders AppNav once auth is off entirely (no currentUser then - see
	// its showChrome derived value) or once auth.currentUser is confirmed non-null - the
	// markup below branches on auth.authEnabled/currentUser itself rather than assuming
	// a user is always present.
	const auth = authContext.get();

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
	// docs/auth.md) and needs a way to actually find where to turn it on.
	const links = $derived(
		[
			{ href: '/', label: 'Logs' },
			{ href: '/traces', label: 'Traces' },
			{ href: '/metrics', label: 'Metrics' },
			{ href: '/ingestion', label: 'Ingestion' },
			{ href: '/indexing', label: 'Indexing' },
			{ href: '/alerts', label: 'Alerts' },
			{ href: '/resources', label: 'Resources' },
			{ href: '/views', label: 'Views' },
			...(!auth.authEnabled || auth.currentUser?.role === 'Admin' ? [{ href: '/auth', label: 'Auth' }] : [])
		]
	);

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
