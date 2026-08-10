// Central reactive state for the Ingestion page - single-query shape (unlike Metrics'
// coupled picker+chart queries), since /api/ingestion/stats returns everything the page
// needs (buckets, tiles, recent-errors) in one call keyed only by a window size.
//
// Unlike every other explorer page, this one polls: Seq's own Ingestion page live-updates
// its tiles/chart, and that's the actual value of an ingestion page - "is something wrong
// right now", not a point-in-time snapshot. Polling (not a WebSocket/live-tail) matches
// this repo's existing precedent that live-tail is reserved for genuinely per-event
// firehoses (logs) - this is a periodic aggregate refresh, the same idiom
// AlertEvaluationWorker/ClickHouseFlushWorker use server-side, just client-side here.

import { getIngestionStats, type IngestionStatsResponse } from '$lib/ingestion-api';

export type IngestionWindowPreset = '15m' | '1h' | '6h' | '24h';

export const INGESTION_WINDOW_PRESETS: { value: IngestionWindowPreset; label: string; minutes: number }[] = [
	{ value: '15m', label: 'Last 15 minutes', minutes: 15 },
	{ value: '1h', label: 'Last hour', minutes: 60 },
	{ value: '6h', label: 'Last 6 hours', minutes: 360 },
	{ value: '24h', label: 'Last 24 hours', minutes: 1440 }
];

const POLL_INTERVAL_MS = 10_000;

export class IngestionState {
	windowPreset = $state<IngestionWindowPreset>('1h');

	stats = $state.raw<IngestionStatsResponse | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	#abort: AbortController | null = null;
	#pollHandle: ReturnType<typeof setInterval> | null = null;

	#minutes(): number {
		return INGESTION_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		// Only show the spinner on the very first load for this window - a background
		// poll refresh shouldn't blank the tiles/chart every 10s while data is already
		// on screen.
		if (!this.stats) this.loading = true;
		this.error = null;
		try {
			const res = await getIngestionStats(this.#minutes(), abort.signal);
			if (abort.signal.aborted) return;
			this.stats = res;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	setWindowPreset(preset: IngestionWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		this.stats = null; // force the spinner - a new window is a genuinely different query, not a background refresh
		void this.load();
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
