// Which dashboard (if any) `/` loads instead of the Logs Explorer - Phase 3's
// "set as home page" (docs-internal/planning/roadmap.md). Per-browser localStorage,
// same shape as `$lib/logs/recent-searches.ts`'s own header comment: dashboards today
// are global/unscoped (no per-user ownership - see DashboardModels.cs's remarks), so
// there's no per-user server-side field to hang this off; a viewer-local preference is
// also just plain simpler for something this low-stakes, and self-heals below if the
// chosen dashboard is later deleted.
import { browser } from '$app/environment';

const STORAGE_KEY = 'flare.dashboards.homeId';

export function getHomeDashboardId(): string | null {
	if (!browser) return null;
	try {
		return localStorage.getItem(STORAGE_KEY);
	} catch {
		return null; // storage disabled (e.g. private browsing) - just means no home dashboard
	}
}

export function setHomeDashboardId(id: string | null): void {
	if (!browser) return;
	try {
		if (id) localStorage.setItem(STORAGE_KEY, id);
		else localStorage.removeItem(STORAGE_KEY);
	} catch {
		// Storage full/disabled - "set as home" silently doesn't stick, nothing else
		// depends on it.
	}
}

/** Called after a dashboard fails to load (e.g. deleted) - clears a stale home
 *  preference pointing at it so `/` falls back to the Logs Explorer next time instead
 *  of repeatedly redirecting into the same "not found" page. */
export function clearHomeDashboardIdIfMatching(id: string): void {
	if (getHomeDashboardId() === id) setHomeDashboardId(null);
}
