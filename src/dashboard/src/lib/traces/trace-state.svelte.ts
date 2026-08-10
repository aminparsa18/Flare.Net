// Reactive state for the trace-detail (waterfall) page - a separate state class from
// TracesExplorerState (the list page), same "one state class per route" convention as
// the rest of this app; the two pages have no shared fields worth forcing together.

import { getTrace, type TraceDto } from '$lib/traces-api';

export class TraceDetailState {
	trace = $state<TraceDto | null>(null);
	loading = $state(false);
	/** Distinguishes "still loading" / "a real fetch error" / "confirmed no such trace" (404) - three different empty-state messages in the page. */
	notFound = $state(false);
	error = $state<string | null>(null);

	selectedSpanId = $state<string | null>(null);
	selectedSpan = $derived(this.trace?.spans.find((s) => s.spanId === this.selectedSpanId) ?? null);

	#loadAbort: AbortController | null = null;

	async load(traceId: string): Promise<void> {
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
			}
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	dispose(): void {
		this.#loadAbort?.abort();
	}
}
