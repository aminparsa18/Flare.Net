<script lang="ts">
	import { stripBase, withBase } from '$lib/paths';
	// Folds what used to be three separate top-level AppNav controls (theme toggle button,
	// LanguageSwitcher's Select, and the raw auth status/logout block) into one dropdown -
	// the page-link row was outgrowing the bar (see nav-links.ts's ever-growing list), and
	// none of these three need to be one click away at all times the way the page links do.
	// LanguageSwitcher.svelte's Select-based picker is gone; its locale-endonym convention
	// moved here unchanged.
	import * as DropdownMenu from '$lib/components/ui/dropdown-menu';
	import * as Command from '$lib/components/ui/command';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { authContext } from '$lib/auth/context';
	import { setMode, userPrefersMode } from 'mode-watcher';
	import { getLocale, setLocale, locales, type Locale } from '$lib/paraglide/runtime';
	import * as m from '$lib/paraglide/messages';
	import EllipsisIcon from '@lucide/svelte/icons/ellipsis';
	import PlugIcon from '@lucide/svelte/icons/plug';
	import KeyRoundIcon from '@lucide/svelte/icons/key-round';
	import ScrollTextIcon from '@lucide/svelte/icons/scroll-text';
	import KeyIcon from '@lucide/svelte/icons/key';
	import SunIcon from '@lucide/svelte/icons/sun';
	import MoonIcon from '@lucide/svelte/icons/moon';
	import MonitorIcon from '@lucide/svelte/icons/monitor';
	import LanguagesIcon from '@lucide/svelte/icons/languages';
	import GlobeIcon from '@lucide/svelte/icons/globe';
	import { displayTimeZone } from '$lib/time/display-zone.svelte';
	import { browserTimeZone, timeZoneOptions } from '$lib/time/time-zone';
	import { formatUtcOffset } from '$lib/time/format';
	import LogOutIcon from '@lucide/svelte/icons/log-out';
	import SparklesIcon from '@lucide/svelte/icons/sparkles';
	import { updateNotice } from '$lib/version/update-notice.svelte';
	import { page } from '$app/state';
	import { navLinks } from './nav-links';
	import type { Component } from 'svelte';
	import EarthIcon from '@lucide/svelte/icons/earth';
	import WorkflowIcon from '@lucide/svelte/icons/workflow';
	import ServerIcon from '@lucide/svelte/icons/server';
	import ContainerIcon from '@lucide/svelte/icons/container';

	const auth = authContext.get();

	const menuLinks = $derived(navLinks(auth).filter((link) => link.inMenu));

	const MENU_LINK_ICONS: Record<string, Component> = {
		'/external-apis': EarthIcon,
		'/pipeline-rules': WorkflowIcon,
		'/hosts': ServerIcon,
		'/kubernetes': ContainerIcon
	};

	function isActive(href: string): boolean {
		const pathname = stripBase(page.url.pathname);
		return pathname === href || pathname.startsWith(`${href}/`);
	}

	// Language names are always shown in their own language (endonyms), never translated
	// through m.*() - a "中文" option shouldn't turn into "Chinese" just because the UI is
	// currently in English. Carried over from LanguageSwitcher.svelte verbatim.
	const localeLabels: Record<Locale, string> = {
		en: 'English',
		'zh-CN': '中文',
		ru: 'Русский'
	};

	// Browser zone and UTC cover the common cases (local debugging, correlating with UTC
	// server logs) one click away; any other IANA zone goes through the searchable dialog
	// below - a ~400-entry radio submenu isn't usable. A named zone picked there stays
	// listed here while it's the active one.
	let zoneDialogOpen = $state(false);
	const namedZone = $derived(
		displayTimeZone.zone !== 'local' && displayTimeZone.zone !== 'UTC' ? displayTimeZone.zone : null
	);
	// Only built while the dialog is open - one Intl lookup per zone for its offset.
	const zoneChoices = $derived(
		zoneDialogOpen ? timeZoneOptions().map((zone) => ({ zone, offset: formatUtcOffset(zone) })) : []
	);

	function pickZone(zone: string) {
		displayTimeZone.set(zone);
		zoneDialogOpen = false;
	}

	async function handleLogout() {
		// No goto() needed here - auth.currentUser flipping to null is itself what
		// +layout.svelte's route-guard $effect reacts to, which calls goto('/login')
		// on its own the moment this resolves.
		await auth.logout();
	}
</script>

<DropdownMenu.Root>
	<DropdownMenu.Trigger>
		{#snippet child({ props })}
			<!-- Icon-only "more" affordance, not a status readout - a trigger that showed the
			     username/"Auth is off" here read as a label rather than something clickable.
			     Auth status still shows first thing inside the menu itself, below. -->
			<!-- Secondary while on one of its `inMenu` pages - the top bar has no link to highlight then. -->
			<Button
				{...props}
				variant={menuLinks.some((link) => isActive(link.href)) ? 'secondary' : 'outline'}
				size="icon-sm"
				aria-label={m.nav_moreOptions()}
			>
				<EllipsisIcon />
			</Button>
		{/snippet}
	</DropdownMenu.Trigger>
	<DropdownMenu.Content class="w-56" align="end">
		<!-- Page links nav-links.ts flags `inMenu` - moved off AppNav's top bar for width. -->
		{#each menuLinks as link (link.href)}
			{@const Icon = MENU_LINK_ICONS[link.href]}
			<DropdownMenu.Item>
				{#snippet child({ props })}
					<a href={withBase(link.href)} {...props} aria-current={isActive(link.href) ? 'page' : undefined}>
						{#if Icon}<Icon />{/if}
						{link.label}
					</a>
				{/snippet}
			</DropdownMenu.Item>
		{/each}
		<DropdownMenu.Separator />
		<!-- /data-sources (the ingest-catalog "how do I get data in" page) is deliberately
		     NOT in nav-links.ts's persistent top bar - see that page's own comment - but a
		     click-to-reveal row here is a fine middle ground between that and its other only
		     entry point (the Logs empty state's "See how to ingest data" link). -->
		<DropdownMenu.Item>
			{#snippet child({ props })}
				<a href={withBase('/data-sources')} {...props}>
					<PlugIcon />
					{m.dataSourcesPage_heading()}
				</a>
			{/snippet}
		</DropdownMenu.Item>
		<!-- Ingest keys (and their ingestion limits, ADR-0051) are Admin-only on the backend -
		     same gate nav-links.ts applies to /auth, including "everyone while auth is off". -->
		{#if !auth.authEnabled || auth.currentUser?.role === 'Admin'}
			<DropdownMenu.Item>
				{#snippet child({ props })}
					<a href={withBase('/ingest-keys')} {...props}>
						<KeyIcon />
						{m.ingestKeysPage_heading()}
					</a>
				{/snippet}
			</DropdownMenu.Item>
			<DropdownMenu.Item>
				{#snippet child({ props })}
					<a href={withBase('/audit-log')} {...props}>
						<ScrollTextIcon />
						{m.auditLogPage_heading()}
					</a>
				{/snippet}
			</DropdownMenu.Item>
		{/if}
		<DropdownMenu.Separator />

		{#if auth.authEnabled}
			<DropdownMenu.Label class="flex items-center justify-between gap-2">
				<span class="truncate">{auth.currentUser?.username}</span>
				<Badge variant="outline">{auth.currentUser?.role}</Badge>
			</DropdownMenu.Label>
			<!-- Personal access tokens (ADR-0019) only make sense once there's a real signed-in
			     identity to own one - same auth.authEnabled gate as the username/role label
			     above, unlike /data-sources' link below which is unconditional. -->
			<DropdownMenu.Item>
				{#snippet child({ props })}
					<a href={withBase('/access-tokens')} {...props}>
						<KeyRoundIcon />
						{m.accessTokensPage_heading()}
					</a>
				{/snippet}
			</DropdownMenu.Item>
			<DropdownMenu.Separator />
		{/if}

		<!-- userPrefersMode (not mode, the resolved light/dark) so "System" shows as the
		     selection while it's following the OS. It reads localStorage, so during SSR it's
		     the default ("system") for one frame, corrected the instant the client hydrates.
		     Harmless: the anti-FOUC script in +layout.svelte already set the *page's* actual
		     theme correctly before paint, this only affects this menu's own selected state. -->
		<DropdownMenu.Label>{m.nav_appearance()}</DropdownMenu.Label>
		<DropdownMenu.RadioGroup value={userPrefersMode.current} onValueChange={(v) => setMode(v as 'light' | 'dark' | 'system')}>
			<DropdownMenu.RadioItem value="light">
				<SunIcon />
				{m.nav_themeLight()}
			</DropdownMenu.RadioItem>
			<DropdownMenu.RadioItem value="dark">
				<MoonIcon />
				{m.nav_themeDark()}
			</DropdownMenu.RadioItem>
			<DropdownMenu.RadioItem value="system">
				<MonitorIcon />
				{m.nav_themeSystem()}
			</DropdownMenu.RadioItem>
		</DropdownMenu.RadioGroup>

		<DropdownMenu.Separator />

		<DropdownMenu.Sub>
			<DropdownMenu.SubTrigger>
				<LanguagesIcon />
				{m.nav_languageLabel()}
				<span class="text-muted-foreground ml-auto">{localeLabels[getLocale()]}</span>
			</DropdownMenu.SubTrigger>
			<DropdownMenu.SubContent>
				<DropdownMenu.RadioGroup value={getLocale()} onValueChange={(v) => v && setLocale(v as Locale)}>
					{#each locales as l (l)}
						<DropdownMenu.RadioItem value={l}>{localeLabels[l]}</DropdownMenu.RadioItem>
					{/each}
				</DropdownMenu.RadioGroup>
			</DropdownMenu.SubContent>
		</DropdownMenu.Sub>

		<DropdownMenu.Sub>
			<DropdownMenu.SubTrigger>
				<GlobeIcon />
				{m.nav_timeZoneLabel()}
				<span class="text-muted-foreground ml-auto">{formatUtcOffset(displayTimeZone.resolved)}</span>
			</DropdownMenu.SubTrigger>
			<DropdownMenu.SubContent>
				<DropdownMenu.RadioGroup value={displayTimeZone.zone} onValueChange={(v) => v && displayTimeZone.set(v)}>
					<DropdownMenu.RadioItem value="local">{m.nav_timeZoneBrowser({ zone: browserTimeZone() })}</DropdownMenu.RadioItem>
					<DropdownMenu.RadioItem value="UTC">UTC</DropdownMenu.RadioItem>
					{#if namedZone}
						<DropdownMenu.RadioItem value={namedZone}>{namedZone}</DropdownMenu.RadioItem>
					{/if}
				</DropdownMenu.RadioGroup>
				<DropdownMenu.Separator />
				<DropdownMenu.Item onSelect={() => (zoneDialogOpen = true)}>{m.nav_timeZoneOther()}</DropdownMenu.Item>
			</DropdownMenu.SubContent>
		</DropdownMenu.Sub>

		<DropdownMenu.Separator />

		{#if auth.authEnabled}
			<DropdownMenu.Item variant="destructive" onSelect={handleLogout} disabled={auth.loading}>
				<LogOutIcon />
				{m.nav_logOut()}
			</DropdownMenu.Item>
		{:else}
			<DropdownMenu.Item>
				{#snippet child({ props })}
					<a href={withBase('/auth')} {...props}>{m.nav_authOff()}</a>
				{/snippet}
			</DropdownMenu.Item>
		{/if}

		<!-- Running version (ADR-0068). A newer release stays reachable here after its banner
		     was dismissed - the item opens UpdateNotice.svelte's release-notes dialog. -->
		{#if updateNotice.info}
			<DropdownMenu.Separator />
			{#if updateNotice.availableRelease}
				<DropdownMenu.Item onSelect={() => (updateNotice.notesOpen = true)}>
					<SparklesIcon class="text-primary" />
					{m.nav_updateAvailable({ latest: updateNotice.availableRelease.version })}
				</DropdownMenu.Item>
			{/if}
			<DropdownMenu.Label class="text-xs font-normal text-muted-foreground">
				{m.nav_version({ version: updateNotice.info.current })}
			</DropdownMenu.Label>
		{/if}
	</DropdownMenu.Content>
</DropdownMenu.Root>

<Command.Dialog bind:open={zoneDialogOpen} title={m.nav_timeZoneDialogTitle()} description={m.nav_timeZoneDialogDescription()}>
	<Command.Input placeholder={m.nav_timeZoneSearchPlaceholder()} />
	<Command.List>
		<Command.Empty>{m.nav_timeZoneNoResults()}</Command.Empty>
		{#each zoneChoices as choice (choice.zone)}
			<Command.Item value={choice.zone} onSelect={() => pickZone(choice.zone)}>
				{choice.zone}
				<span class="text-muted-foreground ml-auto text-xs tabular-nums">{choice.offset}</span>
			</Command.Item>
		{/each}
	</Command.List>
</Command.Dialog>
