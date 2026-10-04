// Per-browser appearance & layout preferences (Settings > Appearance): where the nav lives,
// table density, base font size, reduced motion. Theme itself stays with mode-watcher.
//
// One JSON blob in localStorage, mirrored onto <html data-*> attributes that layout.css
// keys off - and app.html replays the same attributes in an inline script before first
// paint, so density/font size don't flash on load. Client-only for now, same as the other
// display prefs ($lib/time/display-zone). Signed-in state also syncs to the server per user
// (ADR-0110) so prefs follow the user across browsers; localStorage stays the pre-paint cache.
import { browser } from '$app/environment';
import { API_BASE_URL, apiFetch } from '$lib/api';

export const STORAGE_KEY = 'flare.appearance';

export type NavLayout = 'top' | 'sidebar';
export type Density = 'comfortable' | 'compact';
export type FontSize = 'small' | 'default' | 'large';
export type ContentWidth = 'full' | 'centered';

export interface AppearancePrefs {
	navLayout: NavLayout;
	/** Sidebar layout only: icon-only rail instead of icons + labels. */
	sidebarCollapsed: boolean;
	density: Density;
	fontSize: FontSize;
	reduceMotion: boolean;
	contentWidth: ContentWidth;
	/** Monospace font for log message bodies. */
	monoLogs: boolean;
	highContrast: boolean;
}

export const DEFAULTS: AppearancePrefs = {
	navLayout: 'top',
	sidebarCollapsed: false,
	density: 'comfortable',
	fontSize: 'default',
	reduceMotion: false,
	contentWidth: 'full',
	monoLogs: false,
	highContrast: false
};

function pick<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
	return (allowed as readonly unknown[]).includes(value) ? (value as T) : fallback;
}

function parse(s: Partial<Record<keyof AppearancePrefs, unknown>>): AppearancePrefs {
	return {
		navLayout: pick(s.navLayout, ['top', 'sidebar'], DEFAULTS.navLayout),
		sidebarCollapsed: typeof s.sidebarCollapsed === 'boolean' ? s.sidebarCollapsed : DEFAULTS.sidebarCollapsed,
		density: pick(s.density, ['comfortable', 'compact'], DEFAULTS.density),
		fontSize: pick(s.fontSize, ['small', 'default', 'large'], DEFAULTS.fontSize),
		reduceMotion: typeof s.reduceMotion === 'boolean' ? s.reduceMotion : DEFAULTS.reduceMotion,
		contentWidth: pick(s.contentWidth, ['full', 'centered'], DEFAULTS.contentWidth),
		monoLogs: typeof s.monoLogs === 'boolean' ? s.monoLogs : DEFAULTS.monoLogs,
		highContrast: typeof s.highContrast === 'boolean' ? s.highContrast : DEFAULTS.highContrast
	};
}

function load(): AppearancePrefs {
	if (!browser) return { ...DEFAULTS };
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		return raw ? parse(JSON.parse(raw)) : { ...DEFAULTS };
	} catch {
		return { ...DEFAULTS }; // corrupt value or storage disabled
	}
}

const ENDPOINT = `${API_BASE_URL}/api/me/preferences/appearance`;
const PUSH_DELAY_MS = 500;

class AppearanceSettings {
	navLayout = $state<NavLayout>(DEFAULTS.navLayout);
	sidebarCollapsed = $state(DEFAULTS.sidebarCollapsed);
	density = $state<Density>(DEFAULTS.density);
	fontSize = $state<FontSize>(DEFAULTS.fontSize);
	reduceMotion = $state(DEFAULTS.reduceMotion);
	contentWidth = $state<ContentWidth>(DEFAULTS.contentWidth);
	monoLogs = $state(DEFAULTS.monoLogs);
	highContrast = $state(DEFAULTS.highContrast);

	#pushTimer: ReturnType<typeof setTimeout> | undefined;
	#synced = false;

	constructor() {
		this.#assign(load());
	}

	get isDefault(): boolean {
		return (Object.keys(DEFAULTS) as (keyof AppearancePrefs)[]).every((k) => this[k] === DEFAULTS[k]);
	}

	set<K extends keyof AppearancePrefs>(key: K, value: AppearancePrefs[K]): void {
		(this as AppearancePrefs)[key] = value;
		this.#persist();
	}

	reset(): void {
		this.#assign(DEFAULTS);
		this.#persist();
	}

	/**
	 * Pulls the user's server-side prefs (ADR-0110) once per page load. The server copy wins when
	 * it exists; when it doesn't, this browser's current prefs seed it so existing settings
	 * migrate instead of being lost. Best-effort: a failure just leaves local prefs in place.
	 */
	async syncFromServer(): Promise<void> {
		if (!browser || this.#synced) return;
		this.#synced = true;
		try {
			const res = await apiFetch(ENDPOINT);
			if (res.status === 204) {
				if (!this.isDefault) this.#push();
				return;
			}
			if (!res.ok) return;
			this.#assign(parse((await res.json()) as Record<string, unknown>));
			this.#persist(false);
		} catch {
			// Offline / older server without the endpoint - local prefs stay authoritative.
		}
	}

	/** Mirrors the prefs onto <html>; called from the root layout so SSR-hydrated state and the DOM agree. */
	applyToDocument(): void {
		if (!browser) return;
		const d = document.documentElement.dataset;
		d.density = this.density;
		d.fontSize = this.fontSize;
		d.reduceMotion = String(this.reduceMotion);
		d.contentWidth = this.contentWidth;
		d.monoLogs = String(this.monoLogs);
		d.highContrast = String(this.highContrast);
	}

	#assign(p: AppearancePrefs): void {
		this.navLayout = p.navLayout;
		this.sidebarCollapsed = p.sidebarCollapsed;
		this.density = p.density;
		this.fontSize = p.fontSize;
		this.reduceMotion = p.reduceMotion;
		this.contentWidth = p.contentWidth;
		this.monoLogs = p.monoLogs;
		this.highContrast = p.highContrast;
	}

	#snapshot(): AppearancePrefs {
		return {
			navLayout: this.navLayout,
			sidebarCollapsed: this.sidebarCollapsed,
			density: this.density,
			fontSize: this.fontSize,
			reduceMotion: this.reduceMotion,
			contentWidth: this.contentWidth,
			monoLogs: this.monoLogs,
			highContrast: this.highContrast
		};
	}

	#schedulePush(): void {
		if (!browser) return;
		clearTimeout(this.#pushTimer);
		this.#pushTimer = setTimeout(() => this.#push(), PUSH_DELAY_MS);
	}

	#push(): void {
		void apiFetch(ENDPOINT, {
			method: 'PUT',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify(this.#snapshot())
		}).catch(() => {
			// Best-effort; the next change retries.
		});
	}

		#persist(push = true): void {
		this.applyToDocument();
		if (push) this.#schedulePush();
		if (!browser) return;
		try {
			const value = this.#snapshot();
			localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
		} catch {
			// Storage full/disabled - the change still applies for this session.
		}
	}
}

export const appearance = new AppearanceSettings();