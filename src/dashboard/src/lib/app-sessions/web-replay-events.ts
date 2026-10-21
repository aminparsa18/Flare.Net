// Pure helpers for browser session replay (ADR-0264): the shape of an rrweb event as far as Flare looks at it, and the
// two fixes a stream of chunks needs before a Replayer can play it. Kept free of rrweb and Svelte so they stay testable.

/** What Flare reads from an rrweb event; the rest of `data` is rrweb's own and is passed through untouched. */
export interface ReplayEvent {
	type: number;
	timestamp: number;
	data?: unknown;
}

/** rrweb's `EventType.FullSnapshot` and `EventType.Meta`. */
export const FULL_SNAPSHOT = 2;
export const META = 4;

/** Rendering hard limits: a session whose JSON is larger than this is not loaded without asking (see WebReplayState). */
export const LARGE_REPLAY_BYTES = 40 * 1024 * 1024;

function isEvent(value: unknown): value is ReplayEvent {
	if (typeof value !== 'object' || value === null) return false;
	const e = value as Record<string, unknown>;
	return typeof e.type === 'number' && typeof e.timestamp === 'number';
}

/**
 * Joins the chunks (already oldest first) into one list ordered by time. The sort is stable, so events with the same
 * timestamp keep the order the browser recorded them in. Anything that is not an event object is dropped rather than
 * handed to the Replayer, which would throw on it.
 */
export function mergeChunks(chunks: ReadonlyArray<ReadonlyArray<unknown> | null>): ReplayEvent[] {
	const events: ReplayEvent[] = [];
	for (const chunk of chunks) {
		if (!chunk) continue;
		for (const e of chunk) if (isEvent(e)) events.push(e);
	}
	return events.sort((a, b) => a.timestamp - b.timestamp);
}

/**
 * A Replayer needs to start from a full page snapshot. If the first chunk is missing (retention, a query window that
 * began mid-session) the stream starts with incremental events that have nothing to apply to, so drop everything before
 * the first snapshot, keeping the Meta event just before it, which carries the page size. Returns an empty list when
 * there is no snapshot at all.
 */
export function trimToFullSnapshot(events: ReplayEvent[]): ReplayEvent[] {
	const snapshot = events.findIndex((e) => e.type === FULL_SNAPSHOT);
	if (snapshot < 0) return [];
	const start = snapshot > 0 && events[snapshot - 1].type === META ? snapshot - 1 : snapshot;
	return events.slice(start);
}

/** Fits a recorded viewport into a container: shrink to the container's width, never enlarge. */
export function fitScale(containerWidth: number, viewportWidth: number): number {
	if (!(containerWidth > 0) || !(viewportWidth > 0)) return 1;
	return Math.min(1, containerWidth / viewportWidth);
}
