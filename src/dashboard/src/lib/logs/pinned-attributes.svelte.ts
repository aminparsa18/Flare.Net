// Attribute keys a user has pinned in the log event detail sheet (EventDetailSheet) -
// pinned keys render first, in their own "Pinned" table, for every event that has them.
//
// Per-browser preference in localStorage, same as recent-searches.ts and the Logs
// Explorer's collapsed-row flags - no server-side user settings store exists, and a
// display preference like this doesn't warrant one. Unlike recent-searches.ts this is a
// reactive $state singleton rather than a plain read-on-demand helper: toggling a pin must
// re-render the open sheet immediately.
import { browser } from '$app/environment';

function load(storageKey: string): string[] {
	if (!browser) return [];
	try {
		const raw = localStorage.getItem(storageKey);
		if (!raw) return [];
		const parsed: unknown = JSON.parse(raw);
		return Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string') : [];
	} catch {
		return []; // corrupt/foreign value or storage disabled - treat as nothing pinned
	}
}

class PinnedAttributes {
	/** Pin order, oldest first - the Pinned table renders in this order. */
	keys = $state<string[]>([]);

	constructor(private readonly storageKey: string) {
		this.keys = load(storageKey);
	}

	has(key: string): boolean {
		return this.keys.includes(key);
	}

	toggle(key: string): void {
		this.keys = this.has(key) ? this.keys.filter((k) => k !== key) : [...this.keys, key];
		if (!browser) return;
		try {
			localStorage.setItem(this.storageKey, JSON.stringify(this.keys));
		} catch {
			// Storage full/disabled (e.g. private browsing) - the pin still applies for this
			// session, it just won't survive a reload.
		}
	}
}

export const pinnedAttributes = new PinnedAttributes('flare.logs.pinnedAttributeKeys');

// Same mechanism for the trace span detail sheet, with its own key set - span attribute
// keys (http.*, db.*) rarely overlap with what's worth pinning on a log.
export const pinnedSpanAttributes = new PinnedAttributes('flare.traces.pinnedSpanAttributeKeys');
