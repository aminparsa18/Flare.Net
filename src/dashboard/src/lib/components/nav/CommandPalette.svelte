<script lang="ts">
	// Global Cmd+K/Ctrl+K command palette - "Navigate" (every page AppNav links to),
	// "Actions" (Toggle Live Mode + Export Logs, Logs-page-only - see below), "Recently
	// Searched" (last couple of raw search-box terms, also Logs-page-only, see
	// recent-searches.ts), and "Saved Searches" (the cross-page /api/views feature,
	// Logs/Traces/Metrics) in one fuzzy-searchable list. Built on ui/command's
	// Command.Dialog, which already wraps Dialog.Root (Escape/outside-click close for
	// free) and bits-ui's Command primitive (built-in fuzzy scoring - no extra library,
	// no manual filtering needed).
	//
	// `open` is owned one level up (+layout.svelte) rather than here, because AppNav's
	// visible trigger button - a sibling, not a descendant - also needs to open it.
	//
	// "Actions" was originally left out entirely (see the PR that added this file) because
	// this component is mounted at the root layout as a sibling of the routed page, not a
	// descendant, so the page's Svelte context (logsExplorerContext) never reaches it.
	// active-logs-explorer.svelte.ts bridges that gap with a plain module-level singleton
	// that LogsToolbar.svelte registers/clears as the Logs page mounts/unmounts - `logs`
	// below is null on every other page, which hides the group instead of showing
	// controls that would no-op. "Change Environment" and "Jump to Errors" are still out
	// of scope - no Environment/Exceptions concept exists anywhere in this codebase yet.
	import * as Command from '$lib/components/ui/command';
	import { goto } from '$app/navigation';
	import { authContext } from '$lib/auth/context';
	import { navLinks, type NavLink } from './nav-links';
	import { listSavedViews, type SavedView } from '$lib/saved-views-api';
	import { savedViewPath } from '$lib/saved-views/page-paths';
	import { activeLogsExplorer } from '$lib/logs/active-explorer.svelte';
	import { getRecentSearches } from '$lib/logs/recent-searches';
	import type { Component } from 'svelte';

	import ScrollTextIcon from '@lucide/svelte/icons/scroll-text';
	import WaypointsIcon from '@lucide/svelte/icons/waypoints';
	import ChartLineIcon from '@lucide/svelte/icons/chart-line';
	import UploadIcon from '@lucide/svelte/icons/upload';
	import RefreshCwIcon from '@lucide/svelte/icons/refresh-cw';
	import BellIcon from '@lucide/svelte/icons/bell';
	import NetworkIcon from '@lucide/svelte/icons/network';
	import LayoutGridIcon from '@lucide/svelte/icons/layout-grid';
	import ShieldIcon from '@lucide/svelte/icons/shield';
	import StarIcon from '@lucide/svelte/icons/star';
	import RadioIcon from '@lucide/svelte/icons/radio';
	import DownloadIcon from '@lucide/svelte/icons/download';
	import HistoryIcon from '@lucide/svelte/icons/history';

	let { open = $bindable(false) }: { open?: boolean } = $props();

	const auth = authContext.get();
	const links = $derived(navLinks(auth));

	const NAV_ICONS: Record<NavLink['href'], Component> = {
		'/': ScrollTextIcon,
		'/traces': WaypointsIcon,
		'/metrics': ChartLineIcon,
		'/ingestion': UploadIcon,
		'/indexing': RefreshCwIcon,
		'/alerts': BellIcon,
		'/resources': NetworkIcon,
		'/views': LayoutGridIcon,
		'/auth': ShieldIcon
	};

	let views = $state<SavedView[]>([]);
	let loading = $state(false);

	async function loadViews(): Promise<void> {
		loading = true;
		try {
			// No pageType arg - lists across Logs/Traces/Metrics, same as the /views
			// management page - any of those pages could be where the user last saved.
			views = (await listSavedViews()).views;
		} catch {
			views = []; // non-critical - the palette just shows no saved searches until reopened
		} finally {
			loading = false;
		}
	}

	// Same "fetch fresh every open" pattern as SavedSearchesMenu.svelte - cheap,
	// low-cardinality list, keeps it in sync with saves/deletes made from any page
	// without extra plumbing.
	$effect(() => {
		if (open) void loadViews();
	});

	// recent-searches.ts is a sync localStorage read (no fetch, no loading state needed)
	// but still re-read on every open rather than once at module init, so a search typed
	// earlier this same session shows up without requiring the palette to be remounted.
	let recentSearches = $state<string[]>([]);
	$effect(() => {
		if (open) recentSearches = getRecentSearches();
	});

	function selectNav(href: string): void {
		open = false;
		void goto(href);
	}

	function selectView(view: SavedView): void {
		open = false;
		void goto(savedViewPath(view));
	}

	// Only meaningful on the Logs page - Traces/Metrics have no live-tail concept (see
	// their state.svelte.ts files), and the Logs explorer instance itself only exists
	// while that page is mounted. `logs` is null everywhere else, which hides the whole
	// "Actions" group below rather than showing controls that would no-op or throw.
	const logs = $derived(activeLogsExplorer.current);

	function toggleLive(): void {
		if (!logs) return;
		open = false;
		logs.explorer.setLive(!logs.explorer.live);
	}

	function exportLogs(): void {
		if (!logs) return;
		open = false;
		logs.openExport();
	}

	function selectRecentSearch(term: string): void {
		if (!logs) return;
		open = false;
		logs.explorer.setSearch(term);
	}

	function handleKeydown(e: KeyboardEvent): void {
		if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
			e.preventDefault(); // stop the browser's own bookmark-bar/location-bar shortcut
			open = true; // open-only - Escape/outside-click (free via Dialog.Root) are the only close paths
		}
	}
</script>

<svelte:window onkeydown={handleKeydown} />

<Command.Dialog bind:open>
	<Command.Input placeholder="Search or run a command..." />
	<Command.List>
		<Command.Empty>No results found.</Command.Empty>
		<Command.Group heading="Navigate">
			{#each links as link (link.href)}
				{@const Icon = NAV_ICONS[link.href]}
				<Command.Item value={link.label} onSelect={() => selectNav(link.href)}>
					<Icon />
					<span>{link.label}</span>
				</Command.Item>
			{/each}
		</Command.Group>
		{#if logs}
			<Command.Group heading="Actions">
				<Command.Item value="Toggle Live Mode" onSelect={toggleLive}>
					<RadioIcon />
					<span>{logs.explorer.live ? 'Pause live tail' : 'Resume live tail'}</span>
				</Command.Item>
				<Command.Item value="Export Logs" onSelect={exportLogs}>
					<DownloadIcon />
					<span>Export logs…</span>
				</Command.Item>
			</Command.Group>
		{/if}
		{#if logs && recentSearches.length > 0}
			<Command.Group heading="Recently Searched">
				{#each recentSearches as term (term)}
					<Command.Item value="recent {term}" onSelect={() => selectRecentSearch(term)}>
						<HistoryIcon />
						<span class="truncate">{term}</span>
					</Command.Item>
				{/each}
			</Command.Group>
		{/if}
		<Command.Group heading="Saved Searches">
			{#if loading}
				<div class="text-muted-foreground px-2 py-4 text-center text-xs">Loading…</div>
			{:else if views.length === 0}
				<div class="text-muted-foreground px-2 py-4 text-center text-xs">No saved searches yet.</div>
			{:else}
				{#each views as view (view.id)}
					<Command.Item value="{view.name} {view.pageType}" onSelect={() => selectView(view)}>
						<StarIcon />
						<span class="truncate">{view.name}</span>
						<Command.Shortcut>{view.pageType}</Command.Shortcut>
					</Command.Item>
				{/each}
			{/if}
		</Command.Group>
	</Command.List>
</Command.Dialog>
