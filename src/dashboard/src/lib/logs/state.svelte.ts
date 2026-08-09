// Central reactive state for the Logs Explorer - one instance per page, provided via
// `logsExplorerContext` (context.ts) rather than passed as props, per this repo's
// svelte-best-practices skill ("use classes with $state fields... instead of stores",
// "consider context instead of a shared module").

import {
	searchLogs,
	aggregateLogs,
	connectLiveTail,
	type LogEventDto,
	type LogFilter,
	type LiveTailStatus,
	type LiveTailConnection
} from '$lib/api';
import { resolveTimeRange, type TimeRangePreset, type ResolvedTimeRange } from './time-range';

const PAGE_SIZE = 100;

/**
 * Client-side scrollback cap while live-tailing. Deliberately unrelated to
 * `LiveTailOptions.SubscriberChannelCapacity` (500, server-side, per-connection) - that's
 * how many *undelivered* messages the server buffers before reporting drops; this is how
 * much history the browser keeps on screen. Don't "fix" these into one number.
 */
const LIVE_CAP = 2000;

export interface LogsFilterState {
	timeRangePreset: TimeRangePreset;
	customRange: { from: Date; to: Date } | null;
	services: string[];
	severityNumbers: number[];
	search: string;
}

export class LogsExplorerState {
	filter = $state<LogsFilterState>({
		timeRangePreset: '1h',
		customRange: null,
		services: [],
		severityNumbers: [],
		search: ''
	});

	/** Logs Explorer opens in live mode by default - see +page.svelte's onMount, which calls
	 *  startLiveTail() instead of runSearch() when this is true at mount time. */
	live = $state(true);
	connectionStatus = $state<LiveTailStatus>('closed');
	droppedCount = $state(0);

	// Every update below is a wholesale reassignment (fresh search, page append, or live
	// prepend via `[event, ...events]`) - never an in-place mutation of an existing
	// LogEventDto - so $state.raw skips deep-proxying each event (and its three
	// attribute-bag records) for zero fine-grained-reactivity benefit.
	events = $state.raw<LogEventDto[]>([]);
	nextCursor = $state<string | null>(null);

	loading = $state(false);
	loadingMore = $state(false);
	error = $state<string | null>(null);

	selectedEventId = $state<string | null>(null);
	selectedEvent = $derived(this.events.find((e) => e.eventId === this.selectedEventId) ?? null);

	// The API has no "list distinct services" endpoint, so the toolbar's service filter
	// sources its options from a one-off broad aggregate (GroupBy: Service) instead -
	// independent of the current filter/time-range so switching ranges never empties the
	// picklist. Refreshed occasionally (not on every keystroke), not a live subscription.
	knownServices = $state.raw<string[]>([]);

	#seenIds = new Set<string>();
	#connection: LiveTailConnection | null = null;
	#searchAbort: AbortController | null = null;

	/**
	 * Builds a LogFilter from the current service/severity/search selection plus an
	 * explicit time range (or none). Shared by search/load-more (resolved preset range),
	 * live-tail subscribe (no range - the tail endpoint ignores from/to anyway), and
	 * VolumeChart (its own bucket-aware range) so "how the non-time filters map to the
	 * wire shape" lives in exactly one place.
	 */
	buildFilter(range: ResolvedTimeRange | null): LogFilter {
		const filter: LogFilter = {};
		if (range) {
			filter.from = range.from;
			filter.to = range.to;
		}
		if (this.filter.services.length) filter.services = [...this.filter.services];
		if (this.filter.severityNumbers.length) filter.severityNumbers = [...this.filter.severityNumbers];
		if (this.filter.search.trim()) filter.search = this.filter.search.trim();
		return filter;
	}

	#resolvedRange(): ResolvedTimeRange | null {
		return resolveTimeRange(this.filter.timeRangePreset, this.filter.customRange ?? undefined);
	}

	/** One-off, wide-window (7d), single-bucket aggregate just to enumerate service names via `groupKey`. */
	async loadKnownServices(): Promise<void> {
		try {
			const to = new Date();
			const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
			const res = await aggregateLogs({
				filter: { from: from.toISOString(), to: to.toISOString() },
				bucketWidthSeconds: 7 * 24 * 60 * 60,
				groupBy: 'Service'
			});
			this.knownServices = [...new Set(res.buckets.map((b) => b.groupKey).filter((k): k is string => !!k))].sort();
		} catch {
			// Non-critical - the service filter just shows fewer/no options until a retry.
		}
	}

	async runSearch(): Promise<void> {
		this.#searchAbort?.abort();
		const abort = new AbortController();
		this.#searchAbort = abort;

		this.loading = true;
		this.error = null;
		try {
			const res = await searchLogs({ filter: this.buildFilter(this.#resolvedRange()), pageSize: PAGE_SIZE }, abort.signal);
			if (abort.signal.aborted) return;
			this.events = res.events;
			this.nextCursor = res.nextCursor;
			this.#seenIds = new Set(res.events.map((e) => e.eventId));
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	async loadMore(): Promise<void> {
		if (!this.nextCursor || this.loadingMore || this.live) return;
		this.loadingMore = true;
		try {
			const res = await searchLogs({
				filter: this.buildFilter(this.#resolvedRange()),
				cursor: this.nextCursor,
				pageSize: PAGE_SIZE
			});
			const fresh = res.events.filter((e) => !this.#seenIds.has(e.eventId));
			for (const e of fresh) this.#seenIds.add(e.eventId);
			this.events = [...this.events, ...fresh];
			this.nextCursor = res.nextCursor;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loadingMore = false;
		}
	}

	/** Called by every filter setter (time range, services, severities, search). */
	applyFilterChange(): void {
		if (this.live) {
			this.events = [];
			this.nextCursor = null;
			this.#seenIds = new Set();
			this.#connection?.subscribe(this.buildFilter(null));
		} else {
			void this.runSearch();
		}
	}

	setTimeRangePreset(preset: TimeRangePreset): void {
		if (this.live) return; // toolbar disables this control while live; defensive no-op
		this.filter.timeRangePreset = preset;
		if (preset !== 'custom') this.filter.customRange = null;
		this.applyFilterChange();
	}

	setCustomRange(range: { from: Date; to: Date }): void {
		if (this.live) return;
		this.filter.timeRangePreset = 'custom';
		this.filter.customRange = range;
		this.applyFilterChange();
	}

	setServices(services: string[]): void {
		this.filter.services = services;
		this.applyFilterChange();
	}

	setSeverityNumbers(severityNumbers: number[]): void {
		this.filter.severityNumbers = severityNumbers;
		this.applyFilterChange();
	}

	/** Not debounced here - the toolbar's search input owns debounce timing before calling this. */
	setSearch(search: string): void {
		this.filter.search = search;
		this.applyFilterChange();
	}

	#prependLive(event: LogEventDto): void {
		if (this.#seenIds.has(event.eventId)) return; // duplicate delivery (e.g. a re-subscribe race)
		this.#seenIds.add(event.eventId);
		const next = [event, ...this.events];
		if (next.length > LIVE_CAP) {
			const evicted = next.splice(LIVE_CAP);
			for (const e of evicted) this.#seenIds.delete(e.eventId);
		}
		this.events = next;
	}

	#ensureConnection(): LiveTailConnection {
		if (this.#connection) return this.#connection;
		this.#connection = connectLiveTail(this.buildFilter(null), {
			onStatusChange: (status) => (this.connectionStatus = status),
			onEvent: (event) => this.#prependLive(event),
			onDropped: (count) => (this.droppedCount += count),
			onError: (message) => (this.error = message)
		});
		return this.#connection;
	}

	setLive(next: boolean): void {
		if (next === this.live) return;
		this.live = next;

		if (next) {
			this.startLiveTail();
		} else {
			this.#connection?.pause(); // keep the socket warm rather than reconnecting next time
			void this.runSearch(); // fresh static snapshot for the now-current filter
		}
	}

	/**
	 * Connects (or resumes/re-subscribes) the live-tail stream. Split out from setLive so
	 * +page.svelte's onMount can call this directly for the live-by-default initial load -
	 * setLive(true) itself is a no-op when `live` is already true (the equality guard exists
	 * for the toolbar button's toggle, not for starting up).
	 */
	startLiveTail(): void {
		this.events = [];
		this.nextCursor = null;
		this.#seenIds = new Set();
		this.droppedCount = 0;
		this.error = null;
		const connection = this.#ensureConnection();
		// Lazy-connect already subscribes with the current filter on open; an
		// already-open reused connection (paused, then Live re-toggled) needs an
		// explicit re-subscribe in case the filter changed while paused, plus resume.
		connection.subscribe(this.buildFilter(null));
		connection.resume();
	}

	/** Wired to `document.visibilitychange` from the page - spares the server building up drops for a backgrounded tab. */
	handleVisibilityChange(hidden: boolean): void {
		if (!this.live || !this.#connection) return;
		if (hidden) this.#connection.pause();
		else this.#connection.resume();
	}

	dispose(): void {
		this.#searchAbort?.abort();
		this.#connection?.close();
		this.#connection = null;
	}
}
