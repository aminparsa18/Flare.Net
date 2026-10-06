<script lang="ts">
	// Settings shell: a left rail of sections, one route per section (/settings/<section>) so
	// each is deep-linkable. Appearance is the first; the roadmap lists the rest (Regional,
	// Explorer defaults, Account & security, ...).
	import { stripBase, withBase } from '$lib/paths';
	import { page } from '$app/state';
	import { buttonVariants } from '$lib/components/ui/button';
	import { cn } from '$lib/utils';
	import PaletteIcon from '@lucide/svelte/icons/palette';
	import GlobeIcon from '@lucide/svelte/icons/globe';
	import KeyboardIcon from '@lucide/svelte/icons/keyboard';
	import BellIcon from '@lucide/svelte/icons/bell';
	import CompassIcon from '@lucide/svelte/icons/compass';
	import TargetIcon from '@lucide/svelte/icons/target';
	import WorkflowIcon from '@lucide/svelte/icons/workflow';
	import KeyIcon from '@lucide/svelte/icons/key';
	import UserIcon from '@lucide/svelte/icons/user';
	import KeyRoundIcon from '@lucide/svelte/icons/key-round';
	import WrenchIcon from '@lucide/svelte/icons/wrench';
	import PhoneCallIcon from '@lucide/svelte/icons/phone-call';
	import RadarIcon from '@lucide/svelte/icons/radar';
	import ScrollTextIcon from '@lucide/svelte/icons/scroll-text';
	import LayersIcon from '@lucide/svelte/icons/layers';
	import FolderIcon from '@lucide/svelte/icons/folder';
	import { authContext } from '$lib/auth/context';
	import { settingsManagementLinks, workspaceLinks } from '$lib/components/nav/nav-links';
	import { searchSettings } from '$lib/settings/search-index';
	import * as m from '$lib/paraglide/messages';

	const { children } = $props();

	const auth = authContext.get();

	const MANAGEMENT_ICONS = {
		'/settings/slos': TargetIcon,
		'/settings/pipeline-rules': WorkflowIcon,
		'/settings/access-tokens': KeyRoundIcon
	} as const;

	// Admin-only instance pages, rendered under their own "Workspace" heading.
	const WORKSPACE_ICONS = {
		'/settings/channels': BellIcon,
		'/settings/maintenance-windows': WrenchIcon,
		'/settings/oncall-rotations': PhoneCallIcon,
		'/settings/synthetic-monitors': RadarIcon,
		'/settings/audit-log': ScrollTextIcon,
		'/settings/indexing': LayersIcon,
		'/settings/projects': FolderIcon,
		'/settings/ingest-keys': KeyIcon
	} as const;

	const sections = $derived([
		{ href: '/settings/appearance', label: m.settingsAppearance_navLabel(), icon: PaletteIcon },
		{ href: '/settings/regional', label: m.settingsRegional_navLabel(), icon: GlobeIcon },
		{ href: '/settings/explorer', label: m.settingsExplorer_navLabel(), icon: CompassIcon },
		{ href: '/settings/keyboard', label: m.settingsKeyboard_navLabel(), icon: KeyboardIcon },
		{ href: '/settings/notifications', label: m.settingsNotifications_navLabel(), icon: BellIcon },
		// Sessions and data export apply to every signed-in account; the password card
		// inside is local-only.
		...(auth.currentUser
			? [{ href: '/settings/account', label: m.settingsAccount_navLabel(), icon: UserIcon }]
			: []),
		...settingsManagementLinks(auth).map((link) => ({ ...link, icon: MANAGEMENT_ICONS[link.href as keyof typeof MANAGEMENT_ICONS] }))
	]);

	const workspaceSections = $derived(
		workspaceLinks(auth).map((link) => ({ ...link, icon: WORKSPACE_ICONS[link.href as keyof typeof WORKSPACE_ICONS] }))
	);

	// The management tables (SLOs, pipeline rules, tokens, workspace pages) need the full width; the
	// preference forms read better in a narrow column.
	let query = $state('');
	const results = $derived(searchSettings(query));

	const wide = $derived(
		[...Object.keys(MANAGEMENT_ICONS), ...Object.keys(WORKSPACE_ICONS)].some((href) => stripBase(page.url.pathname).startsWith(href))
	);
</script>

{#snippet railLink(section: { href: string; label: string; icon: typeof PaletteIcon })}
	{@const active = stripBase(page.url.pathname).startsWith(section.href)}
	<a
		href={withBase(section.href)}
		aria-current={active ? 'page' : undefined}
		class={cn(buttonVariants({ variant: active ? 'secondary' : 'ghost', size: 'sm' }), 'justify-start')}
	>
		<section.icon class="size-4" />
		{section.label}
	</a>
{/snippet}

<div class="flex h-full flex-col overflow-y-auto md:flex-row">
	<aside class="shrink-0 border-b p-4 md:w-56 md:border-r md:border-b-0">
		<h1 class="mb-3 px-2 text-lg font-semibold">{m.settings_heading()}</h1>
		<input
			type="search"
			bind:value={query}
			placeholder={m.settings_searchPlaceholder()}
			aria-label={m.settings_searchPlaceholder()}
			class="border-input bg-background mb-3 h-8 w-full rounded-md border px-2 text-sm"
		/>
		{#if query.trim()}
			<ul class="flex flex-col gap-1" aria-label={m.settings_searchResults()}>
				{#each results as r (r.href + r.label)}
					<li>
						<a
							href={withBase(r.href)}
							onclick={() => (query = '')}
							class={cn(buttonVariants({ variant: 'ghost', size: 'sm' }), 'h-auto w-full flex-col items-start gap-0 py-1.5')}
						>
							<span>{r.label}</span>
							<span class="text-muted-foreground text-xs">{r.section}</span>
						</a>
					</li>
				{:else}
					<li class="text-muted-foreground px-2 text-sm">{m.settings_searchNoResults()}</li>
				{/each}
			</ul>
		{:else}
		<nav class="flex gap-1 md:flex-col" aria-label={m.settings_heading()}>
			{#each sections as section (section.href)}
				{@render railLink(section)}
			{/each}
			{#if workspaceSections.length}
				<h2 class="text-muted-foreground mt-3 hidden px-2 text-xs font-medium uppercase md:block">
					{m.settings_workspaceHeading()}
				</h2>
				{#each workspaceSections as section (section.href)}
					{@render railLink(section)}
				{/each}
			{/if}
		</nav>
		{/if}
	</aside>
	<main class="min-w-0 flex-1 p-6">
		<div class={cn('mx-auto', !wide && 'max-w-3xl')}>
			{@render children()}
		</div>
	</main>
</div>
