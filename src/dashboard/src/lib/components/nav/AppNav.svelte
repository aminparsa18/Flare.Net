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
		{ href: '/alerts', label: 'Alerts' }
	];
</script>

<nav class="bg-background flex shrink-0 items-center gap-3 border-b px-4 py-2">
	<img src="/logo.png" alt="Flare" class="h-8 w-auto shrink-0" />
	<Separator orientation="vertical" class="h-6" />
	<div class="flex items-center gap-1">
		{#each links as link (link.href)}
			<a
				href={link.href}
				class={cn(buttonVariants({ variant: page.url.pathname === link.href ? 'secondary' : 'ghost', size: 'sm' }))}
			>
				{link.label}
			</a>
		{/each}
	</div>
</nav>
