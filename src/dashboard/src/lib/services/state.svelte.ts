// Central reactive state for the Services landing page - the "which service is
// unhealthy right now" per-service RED-metrics table (see
// docs-internal/planning/roadmap.md's now-removed "Per-service RED-metrics overview"
// item). Polls on the same 10s cadence as IngestionState, same rationale: this is a
// "what's happening right now" view, not a point-in-time snapshot - a stale error rate
// is the one thing this page must never show silently.

import { getServiceOverview, type ServiceMetrics } from '$lib/services-api';
import * as m from '$lib/paraglide/messages';

export type ServicesWindowPreset = '5m' | '15m' | '1h' | '6h' | '24h';

// No `label` field - same reasoning as ingestion/state.svelte.ts's INGESTION_WINDOW_PRESETS:
// a module-scope const can't reflect a per-request/live-switched locale. Use
// servicesWindowPresetLabel() below instead, called fresh at each use.
export const SERVICES_WINDOW_PRESETS: { value: ServicesWindowPreset; minutes: number }[] = [
	{ value: '5m', minutes: 5 },
	{ value: '15m', minutes: 15 },
	{ value: '1h', minutes: 60 },
	{ value: '6h', minutes: 360 },
	{ value: '24h', minutes: 1440 }
];

export function servicesWindowPresetLabel(preset: ServicesWindowPreset): string {
	switch (preset) {
		case '5m':
			return m.servicesWindowPreset_last5m();
		case '15m':
			return m.servicesWindowPreset_last15m();
		case '1h':
			return m.servicesWindowPreset_last1h();
		case '6h':
			return m.servicesWindowPreset_last6h();
		case '24h':
			return m.servicesWindowPreset_last24h();
	}
}

export type ServicesSortColumn = 'serviceName' | 'requestsPerSecond' | 'errorRate' | 'p50DurationMs' | 'p95DurationMs' | 'p99DurationMs';

const POLL_INTERVAL_MS = 10_000;

export class ServicesState {
	windowPreset = $state<ServicesWindowPreset>('15m');
	services = $state.raw<ServiceMetrics[] | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	// Client-side only - the server always returns RequestCount DESC (see
	// ServiceOverviewQueryBuilder's ORDER BY); re-sorting a small, already-fetched list
	// client-side is simpler than threading a sort param through the query, and matches
	// the "server picks a sane default, the table re-sorts freely" precedent
	// IndexingTablesTable already sets for this codebase.
	sortColumn = $state<ServicesSortColumn>('requestsPerSecond');
	sortDescending = $state(true);

	#abort: AbortController | null = null;
	#pollHandle: ReturnType<typeof setInterval> | null = null;

	#minutes(): number {
		return SERVICES_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 15;
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		// Only show the spinner on the very first load for this window - a background
		// poll refresh shouldn't blank the table every 10s while data is already on
		// screen (same reasoning as IngestionState.load).
		if (!this.services) this.loading = true;
		this.error = null;
		try {
			const response = await getServiceOverview(this.#minutes(), abort.signal);
			if (abort.signal.aborted) return;
			this.services = response.services;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	setWindowPreset(preset: ServicesWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		this.services = null; // force the spinner - a new window is a genuinely different query, not a background refresh
		void this.load();
	}

	setSort(column: ServicesSortColumn): void {
		if (this.sortColumn === column) {
			this.sortDescending = !this.sortDescending;
		} else {
			this.sortColumn = column;
			// Service name reads naturally ascending (A-Z); every metric column reads
			// naturally descending (worst/busiest first) - same "pick the useful default
			// direction per column" convention as most sortable-table implementations.
			this.sortDescending = column !== 'serviceName';
		}
	}

	sorted(): ServiceMetrics[] {
		const services = this.services ?? [];
		const column = this.sortColumn;
		const direction = this.sortDescending ? -1 : 1;
		return [...services].sort((a, b) => {
			const left = a[column];
			const right = b[column];
			if (typeof left === 'string' || typeof right === 'string') {
				return direction * String(left).localeCompare(String(right));
			}
			return direction * ((left as number) - (right as number));
		});
	}

	startPolling(): void {
		this.stopPolling();
		this.#pollHandle = setInterval(() => void this.load(), POLL_INTERVAL_MS);
	}

	stopPolling(): void {
		if (this.#pollHandle !== null) {
			clearInterval(this.#pollHandle);
			this.#pollHandle = null;
		}
	}

	dispose(): void {
		this.stopPolling();
		this.#abort?.abort();
	}
}
