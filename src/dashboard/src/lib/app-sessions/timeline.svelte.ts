// Reactive state for the /sessions/[sessionId] timeline - one session's spans in start order, see
// docs-internal/adr/0170-app-session-timeline.md. Loaded once per visit; no polling.

import { getAppSessionTimeline, type AppSessionTimeline } from '$lib/app-sessions-api';

export class AppSessionTimelineState {
	timeline = $state.raw<AppSessionTimeline | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	#abort: AbortController | null = null;

	async load(sessionId: string, fromUnixMs?: number, toUnixMs?: number): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;
		this.loading = true;
		this.error = null;
		try {
			const response = await getAppSessionTimeline(sessionId, { fromUnixMs, toUnixMs }, abort.signal);
			if (abort.signal.aborted) return;
			this.timeline = response;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	dispose(): void {
		this.#abort?.abort();
	}
}

/** Offset of an event from the session's first span, as `+1.23s` / `+450ms`. */
export function formatOffset(ms: number): string {
	if (ms < 1_000) return `+${Math.round(ms)}ms`;
	if (ms < 60_000) return `+${(ms / 1_000).toFixed(2)}s`;
	return `+${Math.floor(ms / 60_000)}m ${Math.round((ms % 60_000) / 1_000)}s`;
}

/** Left and width of an event's bar as percentages of the session's overall span; width has a visible floor. */
export function barGeometry(startMs: number, durationMs: number, originMs: number, totalMs: number): { left: number; width: number } {
	if (totalMs <= 0) return { left: 0, width: 100 };
	const left = Math.min(100, Math.max(0, ((startMs - originMs) / totalMs) * 100));
	const width = Math.max(0.5, Math.min(100 - left, (durationMs / totalMs) * 100));
	return { left, width };
}
