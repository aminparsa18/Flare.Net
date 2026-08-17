// Central reactive state for the Patterns view - one instance per page, provided via
// `patternsContext` (context.ts) rather than passed as props, same
// "context, not a shared module" convention `$lib/logs/state.svelte.ts` already uses.

import { getPatterns, type LogFilter, type LogPatternRow, aggregateLogs } from '$lib/api';
import { resolveTimeRange, type TimeRangePreset, type ResolvedTimeRange } from '$lib/logs/time-range';

export interface PatternsFilterState {
	timeRangePreset: TimeRangePreset;
	customRange: { from: Date; to: Date } | null;
	services: string[];
	severityNumbers: number[];
}

export class PatternsState {
	filter = $state<PatternsFilterState>({
		timeRangePreset: '1h',
		customRange: null,
		services: [],
		severityNumbers: []
	});

	patterns = $state.raw<LogPatternRow[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	// Same "no list-distinct-services endpoint, so source the picklist from a one-off
	// broad aggregate" workaround as LogsExplorerState.knownServices - re-derived here
	// rather than shared, same "no natural shared-utils home yet" call this repo already
	// makes for the per-feature `createContext` helper.
	knownServices = $state.raw<string[]>([]);

	#loadAbort: AbortController | null = null;

	#resolvedRange(): ResolvedTimeRange | null {
		return resolveTimeRange(this.filter.timeRangePreset, this.filter.customRange ?? undefined);
	}

	#buildFilter(): LogFilter {
		const range = this.#resolvedRange();
		const filter: LogFilter = {};
		if (range) {
			filter.from = range.from;
			filter.to = range.to;
		}
		if (this.filter.services.length) filter.services = [...this.filter.services];
		if (this.filter.severityNumbers.length) filter.severityNumbers = [...this.filter.severityNumbers];
		return filter;
	}

	/** One-off, wide-window (7d), single-bucket aggregate just to enumerate service names via `groupKey` - see LogsExplorerState.loadKnownServices. */
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

	async load(): Promise<void> {
		this.#loadAbort?.abort();
		const abort = new AbortController();
		this.#loadAbort = abort;

		this.loading = true;
		this.error = null;
		try {
			const res = await getPatterns({ filter: this.#buildFilter() }, abort.signal);
			if (abort.signal.aborted) return;
			this.patterns = res.patterns;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	setTimeRangePreset(preset: TimeRangePreset): void {
		this.filter.timeRangePreset = preset;
		if (preset !== 'custom') this.filter.customRange = null;
		void this.load();
	}

	setCustomRange(range: { from: Date; to: Date }): void {
		this.filter.timeRangePreset = 'custom';
		this.filter.customRange = range;
		void this.load();
	}

	setServices(services: string[]): void {
		this.filter.services = services;
		void this.load();
	}

	setSeverityNumbers(severityNumbers: number[]): void {
		this.filter.severityNumbers = severityNumbers;
		void this.load();
	}

	dispose(): void {
		this.#loadAbort?.abort();
	}
}
