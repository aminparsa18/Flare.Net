// Per-user regional preferences (Settings > Regional): time format, date order, first day of
// the week and each explorer's default time range. Language and display time zone are owned
// by paraglide and ./time/display-zone, but travel in the same server document (ADR-0110, key
// "regional") so the whole page follows the user across browsers.
//
// localStorage is the cache; ./time/format.ts reads these on every call, so switching
// re-renders every timestamp at once.
import { browser } from '$app/environment';
import { getLocale, setLocale, locales, type Locale } from '$lib/paraglide/runtime';
import { createPrefsSync } from '$lib/prefs-sync';
import { displayTimeZone } from '$lib/time/display-zone.svelte';
import type { TimeRangePreset } from '$lib/logs/time-range';

export const STORAGE_KEY = 'flare.regional';

export type TimeFormat = '24h' | '12h';
export type DateOrder = 'iso' | 'dmy' | 'mdy';
export type WeekStart = 'monday' | 'sunday' | 'saturday';
export type Explorer = 'logs' | 'traces' | 'metrics';

/** Presets offered as a default; fixed durations only, so all three explorers accept them. */
export const DEFAULT_RANGE_CHOICES = ['5m', '15m', '1h', '6h', '24h', '7d', '30d'] as const satisfies readonly TimeRangePreset[];
export type DefaultRange = (typeof DEFAULT_RANGE_CHOICES)[number];

export interface RegionalPrefs {
	timeFormat: TimeFormat;
	dateOrder: DateOrder;
	weekStart: WeekStart;
	defaultRanges: Record<Explorer, DefaultRange>;
}

export const DEFAULTS: RegionalPrefs = {
	timeFormat: '24h',
	dateOrder: 'iso',
	weekStart: 'monday',
	defaultRanges: { logs: '1h', traces: '1h', metrics: '1h' }
};

function pick<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
	return (allowed as readonly unknown[]).includes(value) ? (value as T) : fallback;
}

function parse(s: Record<string, unknown>): RegionalPrefs {
	const r = (s.defaultRanges ?? {}) as Record<string, unknown>;
	return {
		timeFormat: pick(s.timeFormat, ['24h', '12h'], DEFAULTS.timeFormat),
		dateOrder: pick(s.dateOrder, ['iso', 'dmy', 'mdy'], DEFAULTS.dateOrder),
		weekStart: pick(s.weekStart, ['monday', 'sunday', 'saturday'], DEFAULTS.weekStart),
		defaultRanges: {
			logs: pick(r.logs, DEFAULT_RANGE_CHOICES, DEFAULTS.defaultRanges.logs),
			traces: pick(r.traces, DEFAULT_RANGE_CHOICES, DEFAULTS.defaultRanges.traces),
			metrics: pick(r.metrics, DEFAULT_RANGE_CHOICES, DEFAULTS.defaultRanges.metrics)
		}
	};
}

function load(): RegionalPrefs {
	if (!browser) return { ...DEFAULTS, defaultRanges: { ...DEFAULTS.defaultRanges } };
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		if (raw) return parse(JSON.parse(raw));
	} catch {
		// corrupt value or storage disabled
	}
	return { ...DEFAULTS, defaultRanges: { ...DEFAULTS.defaultRanges } };
}

const sync = createPrefsSync('regional');

class RegionalSettings {
	timeFormat = $state<TimeFormat>(DEFAULTS.timeFormat);
	dateOrder = $state<DateOrder>(DEFAULTS.dateOrder);
	weekStart = $state<WeekStart>(DEFAULTS.weekStart);
	defaultRanges = $state<Record<Explorer, DefaultRange>>({ ...DEFAULTS.defaultRanges });

	#synced = false;

	constructor() {
		this.#assign(load());
		// Time zone changes come from the user menu too, so hook the setting rather than its callers.
		displayTimeZone.onChange = () => this.#persist();
	}

	set<K extends 'timeFormat' | 'dateOrder' | 'weekStart'>(key: K, value: RegionalPrefs[K]): void {
		(this as Pick<RegionalPrefs, K>)[key] = value;
		this.#persist();
	}

	setDefaultRange(explorer: Explorer, value: DefaultRange): void {
		this.defaultRanges = { ...this.defaultRanges, [explorer]: value };
		this.#persist();
	}

	/**
	 * Changes the UI language. paraglide's setLocale reloads the page, and on reload the server
	 * copy would win over the cookie - so the new language is pushed first.
	 */
	async changeLocale(locale: Locale): Promise<void> {
		if (locale === getLocale()) return;
		if (browser) await sync.pushNow({ ...this.#snapshot(), locale });
		setLocale(locale);
	}

	/** Pulls the server copy once per page load; it wins when present, otherwise this browser seeds it. */
	async syncFromServer(): Promise<void> {
		if (!browser || this.#synced) return;
		this.#synced = true;
		const doc = await sync.pull();
		if (doc === undefined) return;
		if (doc === null) {
			void sync.pushNow(this.#snapshot());
			return;
		}
		this.#assign(parse(doc));
		this.#saveLocal();
		if (typeof doc.timeZone === 'string') displayTimeZone.set(doc.timeZone, { silent: true });
		if (locales.includes(doc.locale as Locale) && doc.locale !== getLocale()) setLocale(doc.locale as Locale);
	}

	#assign(p: RegionalPrefs): void {
		this.timeFormat = p.timeFormat;
		this.dateOrder = p.dateOrder;
		this.weekStart = p.weekStart;
		this.defaultRanges = { ...p.defaultRanges };
	}

	#snapshot() {
		return {
			timeFormat: this.timeFormat,
			dateOrder: this.dateOrder,
			weekStart: this.weekStart,
			defaultRanges: { ...this.defaultRanges },
			timeZone: displayTimeZone.zone,
			locale: getLocale()
		};
	}

	#saveLocal(): void {
		if (!browser) return;
		try {
			const { timeFormat, dateOrder, weekStart, defaultRanges } = this.#snapshot();
			localStorage.setItem(STORAGE_KEY, JSON.stringify({ timeFormat, dateOrder, weekStart, defaultRanges }));
		} catch {
			// Storage full/disabled - the change still applies for this session.
		}
	}

	#persist(): void {
		this.#saveLocal();
		if (browser) sync.schedulePush(() => this.#snapshot());
	}
}

export const regional = new RegionalSettings();
