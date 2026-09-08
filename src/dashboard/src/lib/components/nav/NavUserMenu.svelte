<script lang="ts">
	// Folds what used to be three separate top-level AppNav controls (theme toggle button,
	// LanguageSwitcher's Select, and the raw auth status/logout block) into one dropdown -
	// the page-link row was outgrowing the bar (see nav-links.ts's ever-growing list), and
	// none of these three need to be one click away at all times the way the page links do.
	// LanguageSwitcher.svelte's Select-based picker is gone; its locale-endonym convention
	// moved here unchanged.
	import * as DropdownMenu from '$lib/components/ui/dropdown-menu';
	import { Button } from '$lib/components/ui/button';
	import { Badge } from '$lib/components/ui/badge';
	import { authContext } from '$lib/auth/context';
	import { mode, setMode } from 'mode-watcher';
	import { getLocale, setLocale, locales, type Locale } from '$lib/paraglide/runtime';
	import * as m from '$lib/paraglide/messages';
	import EllipsisIcon from '@lucide/svelte/icons/ellipsis';
	import PlugIcon from '@lucide/svelte/icons/plug';
	import SunIcon from '@lucide/svelte/icons/sun';
	import MoonIcon from '@lucide/svelte/icons/moon';
	import LanguagesIcon from '@lucide/svelte/icons/languages';
	import LogOutIcon from '@lucide/svelte/icons/log-out';

	const auth = authContext.get();

	// Language names are always shown in their own language (endonyms), never translated
	// through m.*() - a "中文" option shouldn't turn into "Chinese" just because the UI is
	// currently in English. Carried over from LanguageSwitcher.svelte verbatim.
	const localeLabels: Record<Locale, string> = {
		en: 'English',
		'zh-CN': '中文',
		ru: 'Русский'
	};

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
			<Button {...props} variant="outline" size="icon-sm" aria-label={m.nav_moreOptions()}>
				<EllipsisIcon />
			</Button>
		{/snippet}
	</DropdownMenu.Trigger>
	<DropdownMenu.Content class="w-56" align="end">
		<!-- /data-sources (the ingest-catalog "how do I get data in" page) is deliberately
		     NOT in nav-links.ts's persistent top bar - see that page's own comment - but a
		     click-to-reveal row here is a fine middle ground between that and its other only
		     entry point (the Logs empty state's "See how to ingest data" link). -->
		<DropdownMenu.Item>
			{#snippet child({ props })}
				<a href="/data-sources" {...props}>
					<PlugIcon />
					{m.dataSourcesPage_heading()}
				</a>
			{/snippet}
		</DropdownMenu.Item>
		<DropdownMenu.Separator />

		{#if auth.authEnabled}
			<DropdownMenu.Label class="flex items-center justify-between gap-2">
				<span class="truncate">{auth.currentUser?.username}</span>
				<Badge variant="outline">{auth.currentUser?.role}</Badge>
			</DropdownMenu.Label>
			<DropdownMenu.Separator />
		{/if}

		<!-- mode.current is undefined during SSR (mode-watcher's isBrowser guard) - no radio
		     item shows as selected for one frame, corrected the instant the client hydrates.
		     Harmless: the anti-FOUC script in +layout.svelte already set the *page's* actual
		     theme correctly before paint, this only affects this menu's own selected state. -->
		<DropdownMenu.Label>{m.nav_appearance()}</DropdownMenu.Label>
		<DropdownMenu.RadioGroup value={mode.current} onValueChange={(v) => setMode(v as 'light' | 'dark')}>
			<DropdownMenu.RadioItem value="light">
				<SunIcon />
				{m.nav_themeLight()}
			</DropdownMenu.RadioItem>
			<DropdownMenu.RadioItem value="dark">
				<MoonIcon />
				{m.nav_themeDark()}
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

		<DropdownMenu.Separator />

		{#if auth.authEnabled}
			<DropdownMenu.Item variant="destructive" onSelect={handleLogout} disabled={auth.loading}>
				<LogOutIcon />
				{m.nav_logOut()}
			</DropdownMenu.Item>
		{:else}
			<DropdownMenu.Item>
				{#snippet child({ props })}
					<a href="/auth" {...props}>{m.nav_authOff()}</a>
				{/snippet}
			</DropdownMenu.Item>
		{/if}
	</DropdownMenu.Content>
</DropdownMenu.Root>
