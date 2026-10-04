// Export / import of this browser's preferences as one JSON file (Settings > Account).
// Preferences live in localStorage under `flare.*` keys; five of them (appearance, regional,
// explorer, keyboard, notifications) are also per-user server documents (ADR-0110) that win over
// localStorage on the next load, so an import must push those back to the server as well.
import { createPrefsSync } from '$lib/prefs-sync';

const PREFIX = 'flare.';
const FORMAT = 'flare-preferences';
const SYNCED = ['appearance', 'regional', 'explorer', 'keyboard', 'notifications'] as const;

// Transient UI state, not preferences: dismissed notices and half-typed query text.
const EXCLUDED = new Set(['flare.updateNotice.dismissedVersion', 'flare.logs.sqlQueryText']);

export interface PrefsBackup {
	format: typeof FORMAT;
	version: 1;
	exportedAt: string;
	/** localStorage values keyed by full `flare.*` key; values are the stored strings. */
	entries: Record<string, string>;
}

export function exportPrefs(): PrefsBackup {
	const entries: Record<string, string> = {};
	for (let i = 0; i < localStorage.length; i++) {
		const key = localStorage.key(i);
		if (!key?.startsWith(PREFIX) || EXCLUDED.has(key)) continue;
		const value = localStorage.getItem(key);
		if (value !== null) entries[key] = value;
	}
	return { format: FORMAT, version: 1, exportedAt: new Date().toISOString(), entries };
}

/** Parses and validates a backup file's text; throws a readable Error when it isn't one. */
export function parsePrefsBackup(text: string): PrefsBackup {
	let doc: unknown;
	try {
		doc = JSON.parse(text);
	} catch {
		throw new Error('Not valid JSON');
	}
	const d = doc as Partial<PrefsBackup> | null;
	if (!d || d.format !== FORMAT || d.version !== 1 || typeof d.entries !== 'object' || d.entries === null) {
		throw new Error('Not a Flare preferences file');
	}
	const entries: Record<string, string> = {};
	for (const [key, value] of Object.entries(d.entries)) {
		if (key.startsWith(PREFIX) && !EXCLUDED.has(key) && typeof value === 'string') entries[key] = value;
	}
	return { ...(d as PrefsBackup), entries };
}

/** Writes the entries to localStorage and pushes the synced documents to the server. Returns the entry count. */
export async function importPrefs(backup: PrefsBackup): Promise<number> {
	for (const [key, value] of Object.entries(backup.entries)) localStorage.setItem(key, value);
	await Promise.all(
		SYNCED.map(async (name) => {
			const raw = backup.entries[`${PREFIX}${name}`];
			if (raw === undefined) return;
			try {
				await createPrefsSync(name).pushNow(JSON.parse(raw));
			} catch {
				// Unparseable value: localStorage keeps it, the store's own parser falls back to defaults.
			}
		})
	);
	return Object.keys(backup.entries).length;
}
