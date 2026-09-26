// The time zone every timestamp in the dashboard is displayed in - the browser's own zone
// (the default, and what everything rendered in before this setting existed), UTC, or any
// named IANA zone. Picked from the user menu (NavUserMenu.svelte); ./format.ts's formatters
// all read it, so switching re-renders every timestamp on the page at once.
//
// Per-browser preference in localStorage, same as $lib/logs/pinned-attributes - no
// server-side user settings store exists, and a display preference doesn't warrant one.
import { browser } from '$app/environment';
import { browserTimeZone, instantToZoned, zonedToInstant } from './time-zone';

const STORAGE_KEY = 'flare.display.timeZone';

/** 'local' follows the browser's zone (even if the OS zone later changes); anything else is an IANA name, 'UTC' included. */
export type DisplayTimeZone = 'local' | (string & {});

function isValidZone(zone: string): boolean {
	try {
		new Intl.DateTimeFormat('en-US', { timeZone: zone });
		return true;
	} catch {
		return false;
	}
}

function load(): DisplayTimeZone {
	if (!browser) return 'local';
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		// An unknown zone (typo'd by hand, or dropped from a newer tzdata) would make every
		// Intl call below throw - fall back to the browser zone instead.
		return raw && (raw === 'local' || isValidZone(raw)) ? raw : 'local';
	} catch {
		return 'local'; // storage disabled
	}
}

class DisplayTimeZoneSetting {
	zone = $state<DisplayTimeZone>(load());

	/** The concrete IANA zone `zone` stands for - resolves 'local' to the browser's. */
	get resolved(): string {
		return this.zone === 'local' ? browserTimeZone() : this.zone;
	}

	/** True when formatting can use plain local `Date` getters (the hot path for long log tables). */
	get isLocal(): boolean {
		return this.zone === 'local' || this.zone === browserTimeZone();
	}

	set(zone: DisplayTimeZone): void {
		if (zone !== 'local' && !isValidZone(zone)) return;
		this.zone = zone;
		if (!browser) return;
		try {
			localStorage.setItem(STORAGE_KEY, zone);
		} catch {
			// Storage full/disabled - the change still applies for this session.
		}
	}
}

export const displayTimeZone = new DisplayTimeZoneSetting();

/**
 * Midnight, in the display zone, of the calendar day `at` falls on - `daysBack` days
 * earlier when given. "Today"/"This week" start here, so with UTC picked "Today" means the
 * UTC day the server logs are split by, not the browser's.
 */
export function startOfDisplayDay(at: Date, daysBack = 0): Date {
	if (displayTimeZone.isLocal) {
		const d = new Date(at);
		d.setHours(0, 0, 0, 0);
		d.setDate(d.getDate() - daysBack);
		return d;
	}
	const zone = displayTimeZone.resolved;
	const [y, m, d] = instantToZoned(at, zone).slice(0, 10).split('-').map(Number);
	const day = new Date(Date.UTC(y, m - 1, d - daysBack)).toISOString().slice(0, 10);
	return zonedToInstant(`${day}T00:00`, zone);
}

/** Monday-based (ISO 8601) week start, in the display zone, of the week `at` falls in. */
export function startOfDisplayWeek(at: Date): Date {
	let weekday: number; // 0=Sun..6=Sat, as Date#getDay
	if (displayTimeZone.isLocal) {
		weekday = at.getDay();
	} else {
		const [y, m, d] = instantToZoned(at, displayTimeZone.resolved).slice(0, 10).split('-').map(Number);
		weekday = new Date(Date.UTC(y, m - 1, d)).getUTCDay();
	}
	return startOfDisplayDay(at, weekday === 0 ? 6 : weekday - 1);
}
