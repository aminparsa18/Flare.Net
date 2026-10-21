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
/** rrweb's `EventType.Plugin`: console and network capture arrive as these (ADR-0266). */
export const PLUGIN = 6;

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

/** One console call in the recording, at the Unix-ms moment it happened. */
export interface ConsoleEntry {
	unixMs: number;
	level: string;
	text: string;
}

/** One request in the recording. `unixMs` is when it started, worked out from when it finished minus its duration. */
export interface NetworkEntry {
	unixMs: number;
	url: string;
	status: number | undefined;
	durationMs: number | undefined;
	kind: string | undefined;
}

/** Most entries of each kind a recording is read for; a page logging in a loop would otherwise fill the tab. */
export const MAX_CAPTURE_ENTRIES = 2000;

function pluginPayload(event: ReplayEvent, name: string): Record<string, unknown> | undefined {
	if (event.type !== PLUGIN) return undefined;
	const data = event.data as { plugin?: unknown; payload?: unknown } | undefined;
	if (data?.plugin !== name || typeof data.payload !== 'object' || data.payload === null) return undefined;
	return data.payload as Record<string, unknown>;
}

/** The console calls recorded by rrweb's console plugin (`rrweb/console@1`), oldest first. */
export function extractConsole(events: readonly ReplayEvent[]): ConsoleEntry[] {
	const entries: ConsoleEntry[] = [];
	for (const event of events) {
		const payload = pluginPayload(event, 'rrweb/console@1');
		if (!payload || !Array.isArray(payload.payload)) continue;
		// Each argument was stringified by the recorder, objects as JSON and strings with their quotes.
		const text = payload.payload.map((part) => stripQuotes(String(part))).join(' ');
		entries.push({ unixMs: event.timestamp, level: typeof payload.level === 'string' ? payload.level : 'log', text });
		if (entries.length >= MAX_CAPTURE_ENTRIES) break;
	}
	return entries;
}

/** The requests recorded by rrweb's network plugin (`rrweb/network@1`), oldest first. */
export function extractNetwork(events: readonly ReplayEvent[]): NetworkEntry[] {
	const entries: NetworkEntry[] = [];
	for (const event of events) {
		const payload = pluginPayload(event, 'rrweb/network@1');
		if (!payload || !Array.isArray(payload.requests)) continue;
		for (const raw of payload.requests as unknown[]) {
			if (typeof raw !== 'object' || raw === null) continue;
			const r = raw as Record<string, unknown>;
			if (typeof r.name !== 'string') continue;
			const durationMs = typeof r.duration === 'number' && Number.isFinite(r.duration) ? Math.round(r.duration) : undefined;
			entries.push({
				unixMs: event.timestamp - (durationMs ?? 0),
				url: r.name,
				// Browsers that do not report a status give 0.
				status: typeof r.status === 'number' && r.status > 0 ? r.status : undefined,
				durationMs,
				kind: typeof r.initiatorType === 'string' ? r.initiatorType : undefined,
			});
			if (entries.length >= MAX_CAPTURE_ENTRIES) return entries.sort((a, b) => a.unixMs - b.unixMs);
		}
	}
	return entries.sort((a, b) => a.unixMs - b.unixMs);
}

function stripQuotes(value: string): string {
	if (value.length < 2 || !value.startsWith('"') || !value.endsWith('"')) return value;
	try {
		// The recorder JSON-encoded the string. An Error's text arrives encoded twice, so its line breaks are still a backslash and an n.
		const parsed: unknown = JSON.parse(value);
		return typeof parsed === 'string' ? parsed.replaceAll('\\n', '\n') : value;
	} catch {
		return value.slice(1, -1);
	}
}
