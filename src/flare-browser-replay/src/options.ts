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

	/**
	 * `session` (default) records the whole session. `onError` keeps only the last seconds in memory and sends them, with the
	 * seconds after, when an error is reported (`handle.keep()`, or an uncaught error or unhandled rejection with `captureErrors`);
	 * a session without an error sends nothing. Much less to store and to review, at the price of not seeing sessions that
	 * went wrong without throwing.
	 */
	mode?: 'session' | 'onError';
	/** `onError` only: how far back an error's recording reaches. A fresh page snapshot is taken this often, and the last two are kept, so the lead-up is between this and twice this. Default 30000. */
	bufferMs?: number;
	/** `onError` only: how long recording continues after an error. Default 10000. */
	afterErrorMs?: number;
	/** `onError` only: treat an uncaught error or unhandled promise rejection as an error to keep. Default true. */
	captureErrors?: boolean;

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
	mode: 'session' | 'onError';
	bufferMs: number;
	afterErrorMs: number;
	captureErrors: boolean;
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
		mode: options.mode === 'onError' ? 'onError' : 'session',
		bufferMs: Math.max(5000, options.bufferMs ?? 30_000),
		afterErrorMs: Math.max(0, options.afterErrorMs ?? 10_000),
		captureErrors: options.captureErrors ?? true,
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
