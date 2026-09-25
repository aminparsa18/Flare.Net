// Which saved view (if any) each explorer - Logs/Traces/Metrics - restores on a bare
// visit, so reopening the page picks up the saved search the user was last working in
// instead of the hardcoded defaults. Per-browser localStorage, same shape and reasoning
// as `$lib/dashboards/home-preference.ts`'s "set as home page": a low-stakes viewer-local
// preference with no server-side field to hang it off.
//
// Only an explicit pick from the page's saved-view menu (or saving a new one) records
// it - a `?view=` shareable link doesn't, since opening someone's link is a one-off
// visit, not "the search I work in" (ShareViewButton mints those views silently). The
// toolbar's "Clear filters" forgets it, and a deleted view self-heals: the delete paths
// clear it directly, and resolveLastUsedSavedView drops an id that no longer resolves.
import { browser } from '$app/environment';
import { listSavedViews, type PageType, type SavedView } from '$lib/saved-views-api';

const PAGE_TYPES: readonly PageType[] = ['Logs', 'Traces', 'Metrics'];

function storageKey(pageType: PageType): string {
	return `flare.savedViews.lastUsed.${pageType}`;
}

export function getLastUsedViewId(pageType: PageType): string | null {
	if (!browser) return null;
	try {
		return localStorage.getItem(storageKey(pageType));
	} catch {
		return null; // storage disabled (e.g. private browsing) - just means start from defaults
	}
}

export function setLastUsedViewId(pageType: PageType, id: string | null): void {
	if (!browser) return;
	try {
		if (id) localStorage.setItem(storageKey(pageType), id);
		else localStorage.removeItem(storageKey(pageType));
	} catch {
		// Storage full/disabled - the next visit just starts from defaults, nothing else
		// depends on it.
	}
}

/** Called when a saved view is deleted - `id` alone is enough, since the `/views`
 *  management page deletes without a page type in hand. */
export function forgetLastUsedViewId(id: string): void {
	for (const pageType of PAGE_TYPES) {
		if (getLastUsedViewId(pageType) === id) setLastUsedViewId(pageType, null);
	}
}

/**
 * Resolves the remembered view for `pageType`, or `null` when there isn't one. Looks the
 * id up in the page's view list rather than GETting it directly so "deleted" (absent
 * from a successful list - forget it) is told apart from "API unreachable right now"
 * (throws - keep it for next time), which a failed single-view fetch can't do.
 */
export async function resolveLastUsedSavedView(pageType: PageType): Promise<SavedView | null> {
	const id = getLastUsedViewId(pageType);
	if (!id) return null;

	try {
		const { views } = await listSavedViews(pageType);
		const view = views.find((v) => v.id === id) ?? null;
		if (!view) setLastUsedViewId(pageType, null);
		return view;
	} catch (err) {
		console.error(`Failed to restore last-used ${pageType} saved view ${id}:`, err);
		return null;
	}
}
