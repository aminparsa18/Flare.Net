// Per-user notification settings (Settings > Notifications): whether the update-available
// banner shows and whether fired alerts raise a browser notification. Same shape as
// ../explorer/prefs.svelte.ts: localStorage is the cache, the document syncs per user under
// key "notifications" (ADR-0110).
//
// Browser notification *permission* is per browser and can't sync; `browserAlerts` only records
// that this user wants them, and the watcher (alert-watcher.svelte.ts) still checks permission.
import { browser } from '$app/environment';
import { createPrefsSync } from '$lib/prefs-sync';

export const STORAGE_KEY = 'flare.notifications';

export interface NotificationPrefs {
	/** Show the "new version available" strip under the nav. The user-menu version line is unaffected. */
	showUpdateNotice: boolean;
	/** Raise a browser notification when an alert rule starts firing while a Flare tab is open. */
	browserAlerts: boolean;
}

export const DEFAULTS: NotificationPrefs = { showUpdateNotice: true, browserAlerts: false };

function bool(value: unknown, fallback: boolean): boolean {
	return typeof value === 'boolean' ? value : fallback;
}

function parse(s: Record<string, unknown>): NotificationPrefs {
	return {
		showUpdateNotice: bool(s.showUpdateNotice, DEFAULTS.showUpdateNotice),
		browserAlerts: bool(s.browserAlerts, DEFAULTS.browserAlerts)
	};
}

function load(): NotificationPrefs {
	if (!browser) return { ...DEFAULTS };
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		if (raw) return parse(JSON.parse(raw));
	} catch {
		// corrupt value or storage disabled
	}
	return { ...DEFAULTS };
}

const sync = createPrefsSync('notifications');

class NotificationSettings {
	showUpdateNotice = $state(DEFAULTS.showUpdateNotice);
	browserAlerts = $state(DEFAULTS.browserAlerts);

	#synced = false;

	constructor() {
		this.#assign(load());
	}

	set<K extends keyof NotificationPrefs>(key: K, value: NotificationPrefs[K]): void {
		(this as NotificationPrefs)[key] = value;
		this.#persist();
	}

	reset(): void {
		this.#assign(DEFAULTS);
		this.#persist();
	}

	get isDefault(): boolean {
		const s = this.#snapshot();
		return s.showUpdateNotice === DEFAULTS.showUpdateNotice && s.browserAlerts === DEFAULTS.browserAlerts;
	}

	/** Pulls the server copy once per page load; it wins when present, otherwise this browser seeds it. */
	async syncFromServer(): Promise<void> {
		if (!browser || this.#synced) return;
		this.#synced = true;
		const doc = await sync.pull();
		if (doc === undefined) return;
		if (doc === null) {
			if (!this.isDefault) void sync.pushNow(this.#snapshot());
			return;
		}
		this.#assign(parse(doc));
		this.#saveLocal();
	}

	#assign(p: NotificationPrefs): void {
		this.showUpdateNotice = p.showUpdateNotice;
		this.browserAlerts = p.browserAlerts;
	}

	#snapshot(): NotificationPrefs {
		return { showUpdateNotice: this.showUpdateNotice, browserAlerts: this.browserAlerts };
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

export const notificationPrefs = new NotificationSettings();
