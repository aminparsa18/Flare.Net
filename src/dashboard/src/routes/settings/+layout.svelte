<script lang="ts">
	// Settings shell: a left rail of sections, one route per section (/settings/<section>) so
	// each is deep-linkable. Appearance is the first; the roadmap lists the rest (Regional,
	// Explorer defaults, Account & security, ...).
	import { stripBase, withBase } from '$lib/paths';
	import { page } from '$app/state';
	import { buttonVariants } from '$lib/components/ui/button';
	import { cn } from '$lib/utils';
	import PaletteIcon from '@lucide/svelte/icons/palette';
	import * as m from '$lib/paraglide/messages';

	const { children } = $props();

	const sections = $derived([{ href: '/settings/appearance', label: m.settingsAppearance_navLabel(), icon: PaletteIcon }]);
</script>

<div class="flex h-full flex-col overflow-y-auto md:flex-row">
	<aside class="shrink-0 border-b p-4 md:w-56 md:border-r md:border-b-0">
		<h1 class="mb-3 px-2 text-lg font-semibold">{m.settings_heading()}</h1>
		<nav class="flex gap-1 md:flex-col" aria-label={m.settings_heading()}>
			{#each sections as section (section.href)}
				{@const active = stripBase(page.url.pathname).startsWith(section.href)}
				<a
					href={withBase(section.href)}
					aria-current={active ? 'page' : undefined}
					class={cn(buttonVariants({ variant: active ? 'secondary' : 'ghost', size: 'sm' }), 'justify-start')}
				>
					<section.icon class="size-4" />
					{section.label}
				</a>
			{/each}
		</nav>
	</aside>
	<main class="min-w-0 flex-1 p-6">
		<div class="mx-auto max-w-3xl">
			{@render children()}
		</div>
	</main>
</div>
