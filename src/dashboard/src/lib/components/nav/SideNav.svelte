<script lang="ts">
	// The "sidebar" alternative to AppNav's top bar (Settings > Appearance > Navigation).
	// Same links (nav-links.ts, minus the `inMenu` ones that stay in NavUserMenu's dropdown),
	// same palette trigger - laid out as a vertical rail that collapses to icons only.
	import { stripBase, withBase } from '$lib/paths';
	import { page } from '$app/state';
	import { Button, buttonVariants } from '$lib/components/ui/button';
	import { cn } from '$lib/utils';
	import { authContext } from '$lib/auth/context';
	import { appearance } from '$lib/appearance/prefs.svelte';
	import { navLinks } from './nav-links';
	import TerminalModal from './TerminalModal.svelte';
	import NavUserMenu from './NavUserMenu.svelte';
	import SearchIcon from '@lucide/svelte/icons/search';
	import PanelLeftCloseIcon from '@lucide/svelte/icons/panel-left-close';
	import PanelLeftOpenIcon from '@lucide/svelte/icons/panel-left-open';
	import ScrollTextIcon from '@lucide/svelte/icons/scroll-text';
	import RouteIcon from '@lucide/svelte/icons/route';
	import BugIcon from '@lucide/svelte/icons/bug';
	import MailOpenIcon from '@lucide/svelte/icons/mail-open';
	import ChartLineIcon from '@lucide/svelte/icons/chart-line';
	import DatabaseIcon from '@lucide/svelte/icons/database';
	import LayersIcon from '@lucide/svelte/icons/layers';
	import BellIcon from '@lucide/svelte/icons/bell';
	import BoxesIcon from '@lucide/svelte/icons/boxes';
	import LayoutDashboardIcon from '@lucide/svelte/icons/layout-dashboard';
	import BookmarkIcon from '@lucide/svelte/icons/bookmark';
	import ShieldCheckIcon from '@lucide/svelte/icons/shield-check';
	import CircleIcon from '@lucide/svelte/icons/circle';
	import type { Component } from 'svelte';
	import * as m from '$lib/paraglide/messages';

	let { commandPaletteOpen = $bindable(false) }: { commandPaletteOpen?: boolean } = $props();

	const auth = authContext.get();
	const links = $derived(navLinks(auth).filter((link) => !link.inMenu));
	const collapsed = $derived(appearance.sidebarCollapsed);

	const ICONS: Record<string, Component> = {
		'/': ScrollTextIcon,
		'/traces': RouteIcon,
		'/errors': BugIcon,
		'/messaging': MailOpenIcon,
		'/metrics': ChartLineIcon,
		'/ingestion': DatabaseIcon,
		'/indexing': LayersIcon,
		'/alerts': BellIcon,
		'/resources': BoxesIcon,
		'/dashboards': LayoutDashboardIcon,
		'/views': BookmarkIcon,
		'/auth': ShieldCheckIcon
	};

	function isActive(href: string, pathname: string): boolean {
		return href === '/' ? pathname === '/' : pathname === href || pathname.startsWith(`${href}/`);
	}
</script>

<nav
	class={cn('bg-background flex h-full shrink-0 flex-col gap-2 border-r p-2 transition-[width]', collapsed ? 'w-14' : 'w-56')}
	aria-label="Main"
>
	<div class={cn('flex items-center', collapsed ? 'justify-center' : 'justify-between px-1')}>
		{#if !collapsed}
			<img src={withBase('/logo.png')} alt="Flare" class="h-8 w-auto" />
		{/if}
		<Button
			variant="ghost"
			size="icon-sm"
			title={collapsed ? m.settingsAppearance_sidebarExpand() : m.settingsAppearance_sidebarCollapse()}
			aria-label={collapsed ? m.settingsAppearance_sidebarExpand() : m.settingsAppearance_sidebarCollapse()}
			onclick={() => appearance.set('sidebarCollapsed', !collapsed)}
		>
			{#if collapsed}<PanelLeftOpenIcon />{:else}<PanelLeftCloseIcon />{/if}
		</Button>
	</div>

	{#if collapsed}
		<Button variant="outline" size="icon-sm" class="self-center" title={m.nav_searchPlaceholder()} onclick={() => (commandPaletteOpen = true)}>
			<SearchIcon />
		</Button>
	{:else}
		<Button variant="outline" size="sm" class="text-muted-foreground w-full justify-start" onclick={() => (commandPaletteOpen = true)}>
			<SearchIcon data-icon="inline-start" />
			<span class="truncate">{m.nav_searchPlaceholder()}</span>
			<kbd class="bg-muted text-muted-foreground ml-auto rounded border px-1.5 py-0.5 font-mono text-[0.625rem]">⌘K</kbd>
		</Button>
	{/if}

	<div class="flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto">
		{#each links as link (link.href)}
			{@const Icon = ICONS[link.href] ?? CircleIcon}
			{@const active = isActive(link.href, stripBase(page.url.pathname))}
			<a
				href={withBase(link.href)}
				title={collapsed ? link.label : undefined}
				aria-label={collapsed ? link.label : undefined}
				aria-current={active ? 'page' : undefined}
				class={cn(
					buttonVariants({ variant: active ? 'secondary' : 'ghost', size: 'sm' }),
					collapsed ? 'justify-center px-0' : 'justify-start'
				)}
			>
				<Icon class="size-4 shrink-0" />
				{#if !collapsed}<span class="truncate">{link.label}</span>{/if}
			</a>
		{/each}
	</div>

	<div class={cn('flex items-center gap-1', collapsed ? 'flex-col' : 'justify-between')}>
		<NavUserMenu />
		<!-- TerminalModal's trigger carries a top-bar-sized left margin; neutralize it here. -->
		<div class="[&_button]:ml-0">
			<TerminalModal />
		</div>
	</div>
</nav>
