// Per-user keyboard shortcut bindings (Settings > Keyboard). Same shape as the other prefs
// stores: localStorage is the cache, the document syncs per user under key "keyboard"
// (ADR-0110). Only differences from the defaults are stored, so a default changed later still
// reaches users who never touched it.
import { browser } from '$app/environment';
import { createPrefsSync } from '$lib/prefs-sync';
import { DEFAULT_BINDINGS, SHORTCUT_ACTIONS, isValidCombo, type ShortcutAction } from './shortcuts';

export const STORAGE_KEY = 'flare.keyboard';

type Overrides = Partial<Record<ShortcutAction, string>>;

function parse(s: Record<string, unknown>): Overrides {
	const out: Overrides = {};
	for (const id of SHORTCUT_ACTIONS) {
		const v = s[id];
		if (isValidCombo(v) && v !== DEFAULT_BINDINGS[id]) out[id] = v;
	}
	// A hand-edited or stale document must never bind two actions to one combo.
	const seen = new Set<string>();
	for (const id of SHORTCUT_ACTIONS) {
		const combo = out[id] ?? DEFAULT_BINDINGS[id];
		if (seen.has(combo)) return {};
		seen.add(combo);
	}
	return out;
}

function load(): Overrides {
	if (!browser) return {};
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		if (raw) return parse(JSON.parse(raw));
	} catch {
		// corrupt value or storage disabled
	}
	return {};
}

const sync = createPrefsSync('keyboard');

class KeyboardSettings {
	overrides = $state<Overrides>({});

	#synced = false;

	constructor() {
		this.overrides = load();
	}

	combo(id: ShortcutAction): string {
		return this.overrides[id] ?? DEFAULT_BINDINGS[id];
	}

	/** The action already using `combo`, other than `except`, or null. */
	conflict(combo: string, except: ShortcutAction): ShortcutAction | null {
		return SHORTCUT_ACTIONS.find((id) => id !== except && this.combo(id) === combo) ?? null;
	}

	/** Returns false (and changes nothing) when the combo is invalid or already used by another action. */
	set(id: ShortcutAction, combo: string): boolean {
		if (!isValidCombo(combo) || this.conflict(combo, id)) return false;
		const next = { ...this.overrides };
		if (combo === DEFAULT_BINDINGS[id]) delete next[id];
		else next[id] = combo;
		this.overrides = next;
		this.#persist();
		return true;
	}

	reset(id?: ShortcutAction): void {
		if (id === undefined) this.overrides = {};
		else {
			const next = { ...this.overrides };
			delete next[id];
			this.overrides = next;
		}
		this.#persist();
	}

	isDefaultFor(id?: ShortcutAction): boolean {
		return id === undefined ? Object.keys(this.overrides).length === 0 : this.overrides[id] === undefined;
	}

	/** Pulls the server copy once per page load; it wins when present, otherwise this browser seeds it. */
	async syncFromServer(): Promise<void> {
		if (!browser || this.#synced) return;
		this.#synced = true;
		const doc = await sync.pull();
		if (doc === undefined) return;
		if (doc === null) {
			if (!this.isDefaultFor()) void sync.pushNow(this.#snapshot());
			return;
		}
		this.overrides = parse(doc);
		this.#saveLocal();
	}

	#snapshot(): Overrides {
		return { ...this.overrides };
	}

	#saveLocal(): void {
		if (!browser) return;
		try {
			localStorage.setItem(STORAGE_KEY, JSON.stringify(this.#snapshot()));
		} catch {
			// Storage full/disabled - the change still applies for this session.
		}
	}

	#persist(): void {
		this.#saveLocal();
		if (browser) sync.schedulePush(() => this.#snapshot());
	}
}

export const keyboardPrefs = new KeyboardSettings();
