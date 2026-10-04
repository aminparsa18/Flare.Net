// Per-browser appearance & layout preferences (Settings > Appearance): where the nav lives,
// table density, base font size, reduced motion. Theme itself stays with mode-watcher.
//
// One JSON blob in localStorage, mirrored onto <html data-*> attributes that layout.css
// keys off - and app.html replays the same attributes in an inline script before first
// paint, so density/font size don't flash on load. Client-only for now, same as the other
// display prefs ($lib/time/display-zone); server-side per-user storage is on the roadmap.
import { browser } from '$app/environment';

export const STORAGE_KEY = 'flare.appearance';

export type NavLayout = 'top' | 'sidebar';
export type Density = 'comfortable' | 'compact';
export type FontSize = 'small' | 'default' | 'large';

export interface AppearancePrefs {
	navLayout: NavLayout;
	/** Sidebar layout only: icon-only rail instead of icons + labels. */
	sidebarCollapsed: boolean;
	density: Density;
	fontSize: FontSize;
	reduceMotion: boolean;
}

export const DEFAULTS: AppearancePrefs = {
	navLayout: 'top',
	sidebarCollapsed: false,
	density: 'comfortable',
	fontSize: 'default',
	reduceMotion: false
};

function pick<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
	return (allowed as readonly unknown[]).includes(value) ? (value as T) : fallback;
}

function load(): AppearancePrefs {
	if (!browser) return { ...DEFAULTS };
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		if (!raw) return { ...DEFAULTS };
		const s = JSON.parse(raw) as Partial<Record<keyof AppearancePrefs, unknown>>;
		return {
			navLayout: pick(s.navLayout, ['top', 'sidebar'], DEFAULTS.navLayout),
			sidebarCollapsed: typeof s.sidebarCollapsed === 'boolean' ? s.sidebarCollapsed : DEFAULTS.sidebarCollapsed,
			density: pick(s.density, ['comfortable', 'compact'], DEFAULTS.density),
			fontSize: pick(s.fontSize, ['small', 'default', 'large'], DEFAULTS.fontSize),
			reduceMotion: typeof s.reduceMotion === 'boolean' ? s.reduceMotion : DEFAULTS.reduceMotion
		};
	} catch {
		return { ...DEFAULTS }; // corrupt value or storage disabled
	}
}

class AppearanceSettings {
	navLayout = $state<NavLayout>(DEFAULTS.navLayout);
	sidebarCollapsed = $state(DEFAULTS.sidebarCollapsed);
	density = $state<Density>(DEFAULTS.density);
	fontSize = $state<FontSize>(DEFAULTS.fontSize);
	reduceMotion = $state(DEFAULTS.reduceMotion);

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

	/** Mirrors the prefs onto <html>; called from the root layout so SSR-hydrated state and the DOM agree. */
	applyToDocument(): void {
		if (!browser) return;
		const d = document.documentElement.dataset;
		d.density = this.density;
		d.fontSize = this.fontSize;
		d.reduceMotion = String(this.reduceMotion);
	}

	#assign(p: AppearancePrefs): void {
		this.navLayout = p.navLayout;
		this.sidebarCollapsed = p.sidebarCollapsed;
		this.density = p.density;
		this.fontSize = p.fontSize;
		this.reduceMotion = p.reduceMotion;
	}

	#persist(): void {
		this.applyToDocument();
		if (!browser) return;
		try {
			const value: AppearancePrefs = {
				navLayout: this.navLayout,
				sidebarCollapsed: this.sidebarCollapsed,
				density: this.density,
				fontSize: this.fontSize,
				reduceMotion: this.reduceMotion
			};
			localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
		} catch {
			// Storage full/disabled - the change still applies for this session.
		}
	}
}

export const appearance = new AppearanceSettings();
