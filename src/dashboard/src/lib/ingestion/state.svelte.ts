// Central reactive state for the Ingestion page. Two queries as of v10 (was one at v8):
// /api/ingestion/stats ("is data arriving" - buckets/tiles/recent-errors) and
// /api/ingestion/pipeline ("is the buffered pipeline keeping up" - stream/consumer-group
// health, flush-worker health, per-service breakdown), fetched together in one load() call
// on the same window/poll cadence rather than as two independently-polling states - the
// page renders them as one screen, and /api/ingestion/pipeline's own stream/flush sections
// don't even use the window param, so splitting the poll loop in two would buy nothing.
//
// Unlike every other explorer page, this one polls: Seq's own Ingestion page live-updates
// its tiles/chart, and that's the actual value of an ingestion page - "is something wrong
// right now", not a point-in-time snapshot. Polling (not a WebSocket/live-tail) matches
// this repo's existing precedent that live-tail is reserved for genuinely per-event
// firehoses (logs) - this is a periodic aggregate refresh, the same idiom
// AlertEvaluationWorker/ClickHouseFlushWorker use server-side, just client-side here.

import { getIngestionStats, type IngestionProtocol, type IngestionSignal, type IngestionStatsResponse } from '$lib/ingestion-api';
import { getPipelineStats, type PipelineStatsResponse } from '$lib/pipeline-api';

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
	pipeline = $state.raw<PipelineStatsResponse | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	// Set by RejectedTelemetryDialog's "View rejected payloads" action (Planning.md v10
	// follow-up) - scopes the Ingestion log at the bottom of the page to one (signal,
	// protocol) instead of showing every rejection mixed together. Lives here rather than
	// as local state in IngestionLog since it's set from a different component
	// (IngestionSignalsTable's dialog).
	logFilter = $state<{ signal: IngestionSignal; protocol: IngestionProtocol } | null>(null);

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
			const [stats, pipeline] = await Promise.all([
				getIngestionStats(this.#minutes(), abort.signal),
				getPipelineStats(this.#minutes(), abort.signal)
			]);
			if (abort.signal.aborted) return;
			this.stats = stats;
			this.pipeline = pipeline;
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

	setLogFilter(signal: IngestionSignal, protocol: IngestionProtocol): void {
		this.logFilter = { signal, protocol };
	}

	clearLogFilter(): void {
		this.logFilter = null;
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
