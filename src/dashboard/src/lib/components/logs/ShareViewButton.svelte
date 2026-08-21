<script lang="ts">
	// One-click "share the current view" - auto-creates a new saved view from the current
	// filter and copies its `?view=<id>` link, reusing v7's saved-views persistence/
	// hydration wholesale rather than encoding filter state into the URL directly (see
	// Planning.md's "Later" backlog entry for this feature). Always creates a *new* saved
	// view rather than matching an existing one - simpler and more predictable than deep-
	// equality against toSavedViewState()'s nested shape, and doesn't risk silently
	// repurposing a view the user named on purpose. Repeated clicks make repeated saved
	// views, cleaned up the same way any saved search is today (SavedSearchesMenu/`/views`
	// already have delete).
	//
	// Deliberately a standalone button, not folded into SavedSearchesMenu's popover - Share
	// is a single fire-and-forget action, not a list/CRUD surface, so keeping it separate
	// keeps that distinction visible (same reasoning PatternsModal stayed its own trigger
	// rather than living inside another menu).
	import { Button } from '$lib/components/ui/button';
	import { Spinner } from '$lib/components/ui/spinner';
	import Share2Icon from '@lucide/svelte/icons/share-2';
	import CheckIcon from '@lucide/svelte/icons/check';
	import { logsExplorerContext } from '$lib/logs/context';
	import { createSavedView } from '$lib/saved-views-api';
	import { savedViewPath } from '$lib/saved-views/page-paths';
	import { presetLabel } from '$lib/logs/time-range';

	const explorer = logsExplorerContext.get();

	let sharing = $state(false);
	let copied = $state(false);
	let copiedTimeout: ReturnType<typeof setTimeout> | undefined;

	function rangeLabel(): string {
		const { timeRangePreset, customRange } = explorer.filter;
		if (timeRangePreset !== 'custom') {
			return presetLabel(timeRangePreset);
		}
		if (!customRange) return 'a custom range';
		const fmt = (d: Date) => d.toLocaleString(undefined, { hour12: false });
		return `${fmt(customRange.from)} – ${fmt(customRange.to)}`;
	}

	async function handleShare(): Promise<void> {
		if (sharing) return;
		sharing = true;
		try {
			const search = explorer.filter.search.trim();
			const view = await createSavedView({
				name: search ? `Shared: ${search}` : 'Shared logs view',
				description: `Shared on ${new Date().toLocaleString(undefined, { hour12: false })} - ${rangeLabel()}`,
				pageType: 'Logs',
				state: explorer.toSavedViewState()
			});
			await navigator.clipboard.writeText(`${location.origin}${savedViewPath(view)}`);
			copied = true;
			clearTimeout(copiedTimeout);
			copiedTimeout = setTimeout(() => (copied = false), 1500);
		} catch (err) {
			alert(`Couldn't create a shareable link: ${err instanceof Error ? err.message : String(err)}`);
		} finally {
			sharing = false;
		}
	}
</script>

<Button variant="outline" size="icon-sm" title={copied ? 'Copied!' : 'Share this view'} disabled={sharing} onclick={handleShare}>
	{#if sharing}
		<Spinner class="size-3" />
	{:else if copied}
		<CheckIcon />
	{:else}
		<Share2Icon />
	{/if}
</Button>
