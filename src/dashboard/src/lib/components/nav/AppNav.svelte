<script lang="ts">
	// First shared app-shell nav - previously the logo lived inline in LogsToolbar since
	// the whole app was one route. Now that /alerts exists, the logo + page links are
	// hoisted here and mounted once in +layout.svelte so every route shares them; the
	// Logs toolbar keeps only its own filter controls.
	import { page } from '$app/state';
	import { buttonVariants } from '$lib/components/ui/button';
	import { cn } from '$lib/utils';
	import { Separator } from '$lib/components/ui/separator';

	const links = [
		{ href: '/', label: 'Logs' },
		{ href: '/traces', label: 'Traces' },
		{ href: '/alerts', label: 'Alerts' }
	];

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
</nav>
