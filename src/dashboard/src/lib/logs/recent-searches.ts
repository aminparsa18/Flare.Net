// Most-recently-used free-text search terms for the Logs Explorer's search box - a
// lighter-weight, zero-friction cousin of Saved Searches (which requires an explicit
// "save" action and a name). Every debounced commit from the search box (see
// LogsExplorerState.setSearch, its one and only caller) gets remembered here so
// CommandPalette's "Recently Searched" group can offer a quick way back to it.
//
// localStorage rather than a store/context: it needs to survive reloads (a "recent"
// list that resets on every page load isn't useful), and there's exactly one reader
// (CommandPalette, which re-reads fresh on every open, same "fetch fresh" pattern as
// SavedSearchesMenu/loadViews) - no in-app reactivity to wire up.
import { browser } from '$app/environment';

const STORAGE_KEY = 'flare.logs.recentSearches';

/** Hard cap - MRU, oldest evicted first. Deliberately small: this is a "just did this,
 *  want it back" shortcut, not a history log (Saved Searches already covers anything
 *  worth keeping around long-term). */
export const MAX_RECENT_SEARCHES = 2;

export function getRecentSearches(): string[] {
	if (!browser) return [];
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		if (!raw) return [];
		const parsed: unknown = JSON.parse(raw);
		return Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string') : [];
	} catch {
		return []; // corrupt/foreign value under this key - treat as empty rather than throw
	}
}

/** No-op for blank/whitespace-only terms - clearing the search box isn't "a search". */
export function addRecentSearch(term: string): void {
	const trimmed = term.trim();
	if (!trimmed || !browser) return;
	const next = [trimmed, ...getRecentSearches().filter((s) => s !== trimmed)].slice(0, MAX_RECENT_SEARCHES);
	try {
		localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
	} catch {
		// Storage full/disabled (e.g. private browsing) - the recent-searches list is a
		// convenience, not a feature anything else depends on, so just drop it silently.
	}
}
