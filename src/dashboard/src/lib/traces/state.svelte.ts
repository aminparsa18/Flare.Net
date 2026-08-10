// Central reactive state for the Traces list page - mirrors LogsExplorerState's shape
// (`$lib/logs/state.svelte.ts`): a class with `$state` fields, provided via
// `tracesExplorerContext` rather than passed as props. No live-tail here (see
// Planning.md's traces roadmap slice - explicitly out of scope for this pass; the
// waterfall/detail view is the value being built, not a live span firehose), so this is
// simpler than LogsExplorerState: no connection/live/dropped-count fields at all.

import { searchSpans, type SpanDto, type SpanFilter } from '$lib/traces-api';
import { resolveTimeRange, type TimeRangePreset, type ResolvedTimeRange } from '$lib/logs/time-range';

const PAGE_SIZE = 100;

export interface TracesFilterState {
	timeRangePreset: TimeRangePreset;
	services: string[];
}

/** A saved view's `state` payload for `pageType: 'Traces'` - identical to `TracesFilterState` (no `Date`-typed fields here, unlike Logs' `customRange`, so no separate serialized shape is needed). */
export type TracesSavedViewState = TracesFilterState;

export class TracesExplorerState {
	filter = $state<TracesFilterState>({
		timeRangePreset: '1h',
		services: []
	});

	// One row per trace (root spans only) - never mutated in place, always a wholesale
	// reassignment (fresh search or page append), same $state.raw rationale
	// LogsExplorerState documents for its own `events` field.
	traces = $state.raw<SpanDto[]>([]);
	nextCursor = $state<string | null>(null);

	loading = $state(false);
	loadingMore = $state(false);
	error = $state<string | null>(null);

	// No dedicated "list distinct services" endpoint for spans either (same gap
	// LogsExplorerState.knownServices documents for logs, worked around there via a
	// broad aggregate query) - spans has no /api/spans/aggregate yet (deferred as a
	// fast-follow, not required for this roadmap slice), so this instead derives known
	// services from a broad, wide-window search's results. Good enough for a picklist;
	// not a guarantee of completeness.
	knownServices = $state.raw<string[]>([]);

	#seenTraceIds = new Set<string>();
	#searchAbort: AbortController | null = null;

	buildFilter(range: ResolvedTimeRange | null): SpanFilter {
		const filter: SpanFilter = { rootSpansOnly: true };
		if (range) {
			filter.from = range.from;
			filter.to = range.to;
		}
		if (this.filter.services.length) filter.services = [...this.filter.services];
		return filter;
	}

	#resolvedRange(): ResolvedTimeRange | null {
		return resolveTimeRange(this.filter.timeRangePreset);
	}

	/** One-off, wide-window (7d) root-span search just to enumerate service names. */
	async loadKnownServices(): Promise<void> {
		try {
			const to = new Date();
			const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
			const res = await searchSpans({
				filter: { rootSpansOnly: true, from: from.toISOString(), to: to.toISOString() },
				pageSize: 500
			});
			this.knownServices = [...new Set(res.spans.map((s) => s.serviceName).filter(Boolean))].sort();
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
			const res = await searchSpans({ filter: this.buildFilter(this.#resolvedRange()), pageSize: PAGE_SIZE }, abort.signal);
			if (abort.signal.aborted) return;
			this.traces = res.spans;
			this.nextCursor = res.nextCursor;
			this.#seenTraceIds = new Set(res.spans.map((s) => s.traceId));
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	async loadMore(): Promise<void> {
		if (!this.nextCursor || this.loadingMore) return;
		this.loadingMore = true;
		try {
			const res = await searchSpans({
				filter: this.buildFilter(this.#resolvedRange()),
				cursor: this.nextCursor,
				pageSize: PAGE_SIZE
			});
			const fresh = res.spans.filter((s) => !this.#seenTraceIds.has(s.traceId));
			for (const s of fresh) this.#seenTraceIds.add(s.traceId);
			this.traces = [...this.traces, ...fresh];
			this.nextCursor = res.nextCursor;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loadingMore = false;
		}
	}

	setTimeRangePreset(preset: TimeRangePreset): void {
		this.filter.timeRangePreset = preset;
		void this.runSearch();
	}

	setServices(services: string[]): void {
		this.filter.services = services;
		void this.runSearch();
	}

	/** Serializes the current filter into a saved view's opaque `state` payload - see `TracesSavedViewState`. */
	toSavedViewState(): TracesSavedViewState {
		return { timeRangePreset: this.filter.timeRangePreset, services: [...this.filter.services] };
	}

	/** Restores a saved view's filter (defensively narrowed - see `LogsExplorerState.applySavedViewState`'s identical caveat) and re-runs the search. */
	applySavedViewState(state: unknown): void {
		const s = (state ?? {}) as Partial<TracesSavedViewState>;
		this.filter = { timeRangePreset: s.timeRangePreset ?? '1h', services: s.services ?? [] };
		void this.runSearch();
	}

	dispose(): void {
		this.#searchAbort?.abort();
	}
}
