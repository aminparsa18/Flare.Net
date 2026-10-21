/** What `startReplay` accepts. Only `endpoint` and `service` are required. */
export interface ReplayOptions {
	/** Base URL of Flare.Ingest's HTTP receiver, e.g. `https://flare.example.com:4318`. Chunks go to `{endpoint}/v1/replay`. */
	endpoint: string;
	/** The `service.name` the recording belongs to. Must match an ingest key's service list, if the key has one. */
	service: string;
	/** An ingest key, sent as `Authorization: Bearer`. It ships in your JavaScript; restrict it to your site's origins. */
	ingestKey?: string;
	/**
	 * The id that ties the recording to the session in Flare. Defaults to one generated for the browser tab and kept in
	 * `sessionStorage`. To see the replay next to a session's spans, set the same value as the `session.id` span attribute.
	 */
	sessionId?: string;
	/** Share of sessions to record, 0 to 1. Decided once per session. Default 1. */
	sampleRate?: number;

	/** Replace every `<input>`, `<textarea>` and `<select>` value with asterisks. Default true; a password field is always masked. */
	maskAllInputs?: boolean;
	/** Also mask all text on the page. Default false. */
	maskAllText?: boolean;
	/** Elements matching this selector are not recorded at all (replayed as an empty box). Default `[data-flare-block]`. */
	blockSelector?: string;
	/** Text inside elements matching this selector is replaced with asterisks. Default `[data-flare-mask]`. */
	maskTextSelector?: string;

	/** How often a batch is sent, in ms. Default 5000. */
	flushIntervalMs?: number;
	/** A batch is sent early at this many events. Default 500. */
	maxEventsPerChunk?: number;
	/** Stop recording after this long. Default one hour, which is all the dashboard lists. */
	maxDurationMs?: number;
	/** Take a fresh full page snapshot this often, so a replay can start later in a long session. Default off. */
	checkoutEveryMs?: number;

	/** Called with a short reason when the recorder stops by itself (rejected key, over the size limit). */
	onStop?: (reason: string) => void;
}

export interface ResolvedReplayOptions {
	endpoint: string;
	service: string;
	ingestKey?: string;
	sessionId?: string;
	sampleRate: number;
	maskAllInputs: boolean;
	maskAllText: boolean;
	blockSelector: string;
	maskTextSelector: string;
	flushIntervalMs: number;
	maxEventsPerChunk: number;
	maxDurationMs: number;
	checkoutEveryMs: number;
	onStop?: (reason: string) => void;
}

export function resolveOptions(options: ReplayOptions): ResolvedReplayOptions {
	if (!options.endpoint) throw new Error('flare-browser-replay: endpoint is required.');
	if (!options.service) throw new Error('flare-browser-replay: service is required.');
	return {
		endpoint: options.endpoint.replace(/\/+$/, ''),
		service: options.service,
		ingestKey: options.ingestKey,
		sessionId: options.sessionId,
		sampleRate: clamp(options.sampleRate ?? 1, 0, 1),
		maskAllInputs: options.maskAllInputs ?? true,
		maskAllText: options.maskAllText ?? false,
		blockSelector: options.blockSelector ?? '[data-flare-block]',
		maskTextSelector: options.maskTextSelector ?? '[data-flare-mask]',
		flushIntervalMs: Math.max(1000, options.flushIntervalMs ?? 5000),
		maxEventsPerChunk: Math.max(10, options.maxEventsPerChunk ?? 500),
		maxDurationMs: options.maxDurationMs ?? 3_600_000,
		checkoutEveryMs: Math.max(0, options.checkoutEveryMs ?? 0),
		onStop: options.onStop,
	};
}

function clamp(value: number, min: number, max: number): number {
	return Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : max;
}

/** The URL a chunk is posted to. The session id is generated as hex and dashes, but the service name is the app's own. */
export function replayUrl(endpoint: string, service: string, sessionId: string): string {
	return `${endpoint}/v1/replay?service=${encodeURIComponent(service)}&session_id=${encodeURIComponent(sessionId)}`;
}
