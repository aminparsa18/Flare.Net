// The last few custom absolute ranges applied from the time picker, so an incident window
// doesn't have to be re-entered on every page. Per-browser localStorage, same reasoning as
// `$lib/saved-views/last-used.ts`: a low-stakes viewer-local convenience with no
// server-side field to hang it off. Shared across Logs/Traces/Metrics (one key) since an
// incident window is the same wall-clock span whichever explorer is looking at it.
import { browser } from '$app/environment';

const STORAGE_KEY = 'flare.recentCustomRanges';
export const MAX_RECENT_RANGES = 5;

export interface RecentRange {
	from: Date;
	to: Date;
}

export function loadRecentRanges(): RecentRange[] {
	if (!browser) return [];
	try {
		const raw = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]');
		if (!Array.isArray(raw)) return [];
		return raw
			.map((r) => ({ from: new Date(r?.from), to: new Date(r?.to) }))
			.filter((r) => !isNaN(r.from.getTime()) && !isNaN(r.to.getTime()))
			.slice(0, MAX_RECENT_RANGES);
	} catch {
		return []; // storage disabled or corrupt - just means no history
	}
}

/** Puts `range` first (dropping any identical earlier entry), keeps the newest MAX_RECENT_RANGES, returns the new list. */
export function pushRecentRange(range: RecentRange): RecentRange[] {
	const key = (r: RecentRange) => `${r.from.getTime()}-${r.to.getTime()}`;
	const next = [range, ...loadRecentRanges().filter((r) => key(r) !== key(range))].slice(0, MAX_RECENT_RANGES);
	if (browser) {
		try {
			localStorage.setItem(
				STORAGE_KEY,
				JSON.stringify(next.map((r) => ({ from: r.from.toISOString(), to: r.to.toISOString() })))
			);
		} catch {
			// Storage full/disabled - history just isn't kept, nothing else depends on it.
		}
	}
	return next;
}
