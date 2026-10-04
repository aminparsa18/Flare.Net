// Per-user explorer defaults (Settings > Explorer): the starting point for the Logs/Traces/
// Metrics explorers - landing page, log table layout, live-tail behaviour, chart bucket
// interval and facet sidebar. Same shape as ../regional/prefs.svelte.ts: localStorage is the
// cache, the whole document syncs per user under key "explorer" (ADR-0110).
//
// Values are read when an explorer's state is constructed, so a change applies from the next
// page load; saved views and deep links keep carrying their own values.
import { browser } from '$app/environment';
import { createPrefsSync } from '$lib/prefs-sync';
import { BUCKET_WIDTH_OPTIONS_SECONDS, normalizeBucketWidthSeconds } from '$lib/logs/bucket-width';

export const STORAGE_KEY = 'flare.explorer';

export const LANDING_PAGES = ['default', 'logs', 'traces', 'metrics', 'errors'] as const;
export type LandingPage = (typeof LANDING_PAGES)[number];
export const LINES_PER_ROW_CHOICES = [1, 2, 3, 5, 10] as const;
export const ROW_CLICK_ACTIONS = ['panel', 'inline'] as const;
export type RowClickAction = (typeof ROW_CLICK_ACTIONS)[number];
export const LIVE_BUFFER_CHOICES = [500, 1000, 2000, 5000] as const;
export { BUCKET_WIDTH_OPTIONS_SECONDS };

export interface ExplorerPrefs {
	/** Where a bare "/" lands; 'default' keeps the home dashboard (if set), else Logs. */
	landingPage: LandingPage;
	linesPerRow: number;
	/** Clicking a log row: details in the side panel, or expanded inside the row itself. */
	rowClickAction: RowClickAction;
	showTimeColumn: boolean;
	showMessageColumn: boolean;
	/** Logs open in live-tail mode rather than running a one-off search. */
	liveByDefault: boolean;
	/** Live tail: the newest row pushes in while the view is at the top; off keeps whatever you are looking at still. */
	liveAutoScroll: boolean;
	/** How many rows the browser keeps while live-tailing. */
	liveBuffer: number;
	/** Chart bucket width in seconds for Logs and Metrics; null = auto. */
	bucketWidthSeconds: number | null;
	/** Whether the facet sidebar starts open on a page that has no stored choice of its own. */
	facetSidebarOpen: boolean;
}

export const DEFAULTS: ExplorerPrefs = {
	landingPage: 'default',
	linesPerRow: 1,
	rowClickAction: 'panel',
	showTimeColumn: true,
	showMessageColumn: true,
	liveByDefault: true,
	liveAutoScroll: true,
	liveBuffer: 2000,
	bucketWidthSeconds: null,
	facetSidebarOpen: true
};

function pick<T>(value: unknown, allowed: readonly T[], fallback: T): T {
	return (allowed as readonly unknown[]).includes(value) ? (value as T) : fallback;
}

function bool(value: unknown, fallback: boolean): boolean {
	return typeof value === 'boolean' ? value : fallback;
}

function parse(s: Record<string, unknown>): ExplorerPrefs {
	return {
		landingPage: pick(s.landingPage, LANDING_PAGES, DEFAULTS.landingPage),
		linesPerRow: pick(s.linesPerRow, LINES_PER_ROW_CHOICES, DEFAULTS.linesPerRow),
		rowClickAction: pick(s.rowClickAction, ROW_CLICK_ACTIONS, DEFAULTS.rowClickAction),
		showTimeColumn: bool(s.showTimeColumn, DEFAULTS.showTimeColumn),
		showMessageColumn: bool(s.showMessageColumn, DEFAULTS.showMessageColumn),
		liveByDefault: bool(s.liveByDefault, DEFAULTS.liveByDefault),
		liveAutoScroll: bool(s.liveAutoScroll, DEFAULTS.liveAutoScroll),
		liveBuffer: pick(s.liveBuffer, LIVE_BUFFER_CHOICES, DEFAULTS.liveBuffer),
		bucketWidthSeconds: normalizeBucketWidthSeconds(s.bucketWidthSeconds),
		facetSidebarOpen: bool(s.facetSidebarOpen, DEFAULTS.facetSidebarOpen)
	};
}

function load(): ExplorerPrefs {
	if (!browser) return { ...DEFAULTS };
	try {
		const raw = localStorage.getItem(STORAGE_KEY);
		if (raw) return parse(JSON.parse(raw));
	} catch {
		// corrupt value or storage disabled
	}
	return { ...DEFAULTS };
}

const sync = createPrefsSync('explorer');

class ExplorerSettings {
	landingPage = $state<LandingPage>(DEFAULTS.landingPage);
	linesPerRow = $state(DEFAULTS.linesPerRow);
	rowClickAction = $state<RowClickAction>(DEFAULTS.rowClickAction);
	showTimeColumn = $state(DEFAULTS.showTimeColumn);
	showMessageColumn = $state(DEFAULTS.showMessageColumn);
	liveByDefault = $state(DEFAULTS.liveByDefault);
	liveAutoScroll = $state(DEFAULTS.liveAutoScroll);
	liveBuffer = $state(DEFAULTS.liveBuffer);
	bucketWidthSeconds = $state<number | null>(DEFAULTS.bucketWidthSeconds);
	facetSidebarOpen = $state(DEFAULTS.facetSidebarOpen);

	#synced = false;

	constructor() {
		this.#assign(load());
	}

	set<K extends keyof ExplorerPrefs>(key: K, value: ExplorerPrefs[K]): void {
		(this as ExplorerPrefs)[key] = value;
		this.#persist();
	}

	reset(keys?: readonly (keyof ExplorerPrefs)[]): void {
		if (!keys) this.#assign(DEFAULTS);
		else for (const k of keys) (this as ExplorerPrefs)[k] = DEFAULTS[k] as never;
		this.#persist();
	}

	/** True when the given settings (default: all) are at their defaults - disables a reset button. */
	isDefaultFor(keys: readonly (keyof ExplorerPrefs)[] = Object.keys(DEFAULTS) as (keyof ExplorerPrefs)[]): boolean {
		const s = this.#snapshot();
		return keys.every((k) => s[k] === DEFAULTS[k]);
	}

	/** True when every setting is at its default. */
	get isDefault(): boolean {
		return this.isDefaultFor();
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

	#assign(p: ExplorerPrefs): void {
		this.landingPage = p.landingPage;
		this.linesPerRow = p.linesPerRow;
		this.rowClickAction = p.rowClickAction;
		this.showTimeColumn = p.showTimeColumn;
		this.showMessageColumn = p.showMessageColumn;
		this.liveByDefault = p.liveByDefault;
		this.liveAutoScroll = p.liveAutoScroll;
		this.liveBuffer = p.liveBuffer;
		this.bucketWidthSeconds = p.bucketWidthSeconds;
		this.facetSidebarOpen = p.facetSidebarOpen;
	}

	#snapshot(): ExplorerPrefs {
		return {
			landingPage: this.landingPage,
			linesPerRow: this.linesPerRow,
			rowClickAction: this.rowClickAction,
			showTimeColumn: this.showTimeColumn,
			showMessageColumn: this.showMessageColumn,
			liveByDefault: this.liveByDefault,
			liveAutoScroll: this.liveAutoScroll,
			liveBuffer: this.liveBuffer,
			bucketWidthSeconds: this.bucketWidthSeconds,
			facetSidebarOpen: this.facetSidebarOpen
		};
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

export const explorerPrefs = new ExplorerSettings();
