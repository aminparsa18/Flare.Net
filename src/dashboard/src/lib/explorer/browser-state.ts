// Clears the small per-browser conveniences listed under Settings > Explorer. Each one is a
// plain localStorage key (or key family) owned by the feature that writes it; this only
// removes them, so the feature falls back to its default on next use.
import { browser } from '$app/environment';

function removeMatching(test: (key: string) => boolean): void {
	if (!browser) return;
	try {
		const keys: string[] = [];
		for (let i = 0; i < localStorage.length; i++) {
			const key = localStorage.key(i);
			if (key && test(key)) keys.push(key);
		}
		for (const key of keys) localStorage.removeItem(key);
	} catch {
		// Storage disabled - nothing to clear.
	}
}

/** Filter rows and charts remember being collapsed/expanded under `flare.<page>.<name>Collapsed`. */
export const clearCollapsedFlags = () => removeMatching((k) => /^flare\..+Collapsed$/.test(k));
export const clearRecentSearches = () => removeMatching((k) => k === 'flare.logs.recentSearches');
export const clearRecentCustomRanges = () => removeMatching((k) => k === 'flare.recentCustomRanges');
export const clearLastUsedViews = () => removeMatching((k) => k.startsWith('flare.savedViews.lastUsed.'));
