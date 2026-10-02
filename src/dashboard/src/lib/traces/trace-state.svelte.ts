// Reactive state for the trace-detail (waterfall) page - a separate state class from
// TracesExplorerState (the list page), same "one state class per route" convention as
// the rest of this app; the two pages have no shared fields worth forcing together.

import { getTrace, getTraceChildren, type TraceDto } from '$lib/traces-api';
import { buildSpanTree, spanMatchesSearch } from '$lib/traces/span-tree';

export class TraceDetailState {
	trace = $state<TraceDto | null>(null);
	loading = $state(false);
	/** Distinguishes "still loading" / "a real fetch error" / "confirmed no such trace" (404) - three different empty-state messages in the page. */
	notFound = $state(false);
	error = $state<string | null>(null);

	/** Span ids with children not loaded yet (large traces load lazily by depth). */
	partialSpanIds = $state<Set<string>>(new Set());
	/** Span ids whose children are being fetched right now. */
	loadingChildIds = $state<Set<string>>(new Set());
	/** A failed `loadChildren` - shown inline rather than replacing the whole page like `error`. */
	childLoadError = $state<string | null>(null);

	selectedSpanId = $state<string | null>(null);
	selectedSpan = $derived(this.trace?.spans.find((s) => s.spanId === this.selectedSpanId) ?? null);

	/** Pre-order (waterfall) render order - shared so search steps through matches top to bottom. */
	spanTree = $derived(buildSpanTree(this.trace?.spans ?? []));

	// Span search box over the waterfall and flame graph. Lives here rather than in either
	// view so the query and the current match survive switching between the two tabs.
	// Stepping through matches only scrolls/highlights - it doesn't set selectedSpanId,
	// because selecting opens the modal SpanDetailSheet, which would cover the next match.
	spanSearch = $state('');
	/** Matching span ids, in waterfall order. Empty when the box is empty. */
	spanSearchMatches = $derived(
		this.spanSearch.trim() ? this.spanTree.filter((r) => spanMatchesSearch(r.span, this.spanSearch)).map((r) => r.span.spanId) : []
	);
	spanSearchMatchSet = $derived(new Set(this.spanSearchMatches));
	spanSearchActive = $derived(this.spanSearch.trim() !== '');
	/** Index into spanSearchMatches of the "current" match the views scroll to; reset to the first match whenever the query changes. */
	spanSearchIndex = $state(0);
	focusedMatchId = $derived(this.spanSearchMatches[this.spanSearchIndex] ?? null);

	setSpanSearch(query: string): void {
		this.spanSearch = query;
		this.spanSearchIndex = 0;
	}

	stepSpanSearch(delta: 1 | -1): void {
		const n = this.spanSearchMatches.length;
		if (n === 0) return;
		this.spanSearchIndex = (this.spanSearchIndex + delta + n) % n;
	}

	#loadAbort: AbortController | null = null;

	/**
	 * `initialSpanId` is the `?span=` deep-link param - selected in the same update that
	 * lands the trace (not afterwards by the caller), so the waterfall's first render
	 * already sees it and can scroll it into view on mount. Ignored if it isn't a span
	 * of this trace.
	 */
	async load(traceId: string, initialSpanId?: string | null): Promise<void> {
		this.#loadAbort?.abort();
		const abort = new AbortController();
		this.#loadAbort = abort;

		this.loading = true;
		this.notFound = false;
		this.error = null;
		try {
			const result = await getTrace(traceId, abort.signal);
			if (abort.signal.aborted) return;
			if (result === null) {
				this.notFound = true;
			} else {
				this.trace = result;
				this.partialSpanIds = new Set(result.partialSpanIds);
				this.loadingChildIds = new Set();
				if (initialSpanId && result.spans.some((s) => s.spanId === initialSpanId)) this.selectedSpanId = initialSpanId;
			}
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	/** Fetches the next levels below a partially loaded span and merges them into the trace. */
	async loadChildren(spanId: string): Promise<void> {
		const trace = this.trace;
		if (!trace || !this.partialSpanIds.has(spanId) || this.loadingChildIds.has(spanId)) return;
		this.loadingChildIds = new Set(this.loadingChildIds).add(spanId);
		this.childLoadError = null;
		try {
			const result = await getTraceChildren(trace.traceId, spanId, this.#loadAbort?.signal);
			if (this.trace !== trace) return; // a different trace was loaded meanwhile
			const have = new Set(trace.spans.map((s) => s.spanId));
			const merged = [...trace.spans, ...result.spans.filter((s) => !have.has(s.spanId))];
			merged.sort((a, b) => a.startTime.localeCompare(b.startTime));
			this.trace = { ...trace, spans: merged };
			const partial = new Set(this.partialSpanIds);
			partial.delete(spanId);
			for (const id of result.partialSpanIds) partial.add(id);
			this.partialSpanIds = partial;
		} catch (err) {
			if (this.trace !== trace) return;
			this.childLoadError = err instanceof Error ? err.message : String(err);
		} finally {
			if (this.trace === trace) {
				const loading = new Set(this.loadingChildIds);
				loading.delete(spanId);
				this.loadingChildIds = loading;
			}
		}
	}

	dispose(): void {
		this.#loadAbort?.abort();
	}
}
