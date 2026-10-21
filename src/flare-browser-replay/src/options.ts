import { DEFAULT_BODY_LIMIT, type NetworkDetail } from './sanitize.js';

export type ConsoleLevel = 'log' | 'info' | 'warn' | 'error' | 'debug';

const CONSOLE_LEVELS: readonly ConsoleLevel[] = ['log', 'info', 'warn', 'error', 'debug'];

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

	/**
	 * Record the page's console output so the replay shows it beside the video. Off by default, because console output often
	 * carries data the page would never display. Give the levels to record (`['warn', 'error']` is the usual choice), or `true`
	 * for `warn` and `error`. Uncaught errors and unhandled rejections are recorded as `error` entries. Arguments are cut to
	 * 500 characters and 3 levels deep. Nothing in the console is masked, so do not log what you would not store.
	 */
	captureConsole?: boolean | ConsoleLevel[];
	/**
	 * Record the page's `fetch`, `XMLHttpRequest` and beacon requests (URL, status, timing) so the replay shows them beside the
	 * video. Off by default. Headers and bodies are recorded only through `networkDetail`. The query string and fragment are dropped from the URL unless
	 * `networkKeepQuery` is set, and the recorder's own uploads are left out.
	 */
	captureNetwork?: boolean;
	/** Keep the query string and fragment of recorded request URLs. Default false: they often carry tokens and ids. */
	networkKeepQuery?: boolean;
	/**
	 * With `captureNetwork`: also record each request's method, and optionally headers and bodies. Off by default, and it makes
	 * the recorder wrap `fetch` and `XMLHttpRequest` (the plain list does not). `true` records the method only.
	 * `headers` is an allowlist of header names to keep for both the request and the response; nothing else is recorded, names
	 * that look like credentials (authorization, cookie, anything with token, secret, key, session) are refused even if listed,
	 * and values are cut to 200 characters. `bodies` keeps the request and response body of a request that failed (status 400
	 * or above), only when it is JSON, with values under keys that look like secrets (password, token, card and the like)
	 * replaced and the text cut to `bodyLimit` characters (default 2048, 256 to 8192). Other values in a body are stored as sent.
	 */
	networkDetail?: boolean | { headers?: string[]; bodies?: boolean; bodyLimit?: number };
	/**
	 * Record what `<canvas>` elements show (charts, maps, drawings, WebGL). Off by default: pixels cannot be masked the way text
	 * can, so a canvas that draws personal data would be stored as drawn. Give a number of frames per second (1 to 10) or `true`
	 * for 1. A canvas that has not changed sends nothing; frames are scaled to 800 px and compressed. A canvas inside an element
	 * matching `blockSelector` or `maskTextSelector` is never recorded, and `maskAllText` turns canvas capture off.
	 */
	captureCanvas?: boolean | number;

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
	captureConsole: ConsoleLevel[];
	captureNetwork: boolean;
	networkKeepQuery: boolean;
	/** Undefined when only the plain list is wanted. */
	networkDetail: NetworkDetail | undefined;
	/** Frames per second, 0 when canvas capture is off. */
	canvasFps: number;
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
		captureConsole: consoleLevels(options.captureConsole),
		captureNetwork: options.captureNetwork ?? false,
		networkKeepQuery: options.networkKeepQuery ?? false,
		networkDetail: options.captureNetwork ? networkDetail(options.networkDetail) : undefined,
		// Masking all text is a promise that nothing readable is stored, which a canvas of rendered text would break.
		canvasFps: options.maskAllText ? 0 : canvasFps(options.captureCanvas),
		onStop: options.onStop,
	};
}

/** `true` is 1 frame per second; a number is clamped to 1 to 10; anything else is off. */
function canvasFps(value: ReplayOptions['captureCanvas']): number {
	if (value === true) return 1;
	if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) return 0;
	return Math.min(10, Math.max(1, value));
}

function networkDetail(value: ReplayOptions['networkDetail']): NetworkDetail | undefined {
	if (!value) return undefined;
	const o = value === true ? {} : value;
	const headers = Array.isArray(o.headers) ? o.headers.filter((h): h is string => typeof h === 'string' && h.trim() !== '').map((h) => h.trim().toLowerCase()) : [];
	return { headers: [...new Set(headers)], bodies: o.bodies === true, bodyLimit: clamp(o.bodyLimit ?? DEFAULT_BODY_LIMIT, 256, 8192) };
}

/** `true` is warn and error; a list is cut to the levels the recorder knows; anything else is off. */
function consoleLevels(value: ReplayOptions['captureConsole']): ConsoleLevel[] {
	if (value === true) return ['warn', 'error'];
	if (!Array.isArray(value)) return [];
	return CONSOLE_LEVELS.filter((level) => value.includes(level));
}

function clamp(value: number, min: number, max: number): number {
	return Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : max;
}

/** The URL a chunk is posted to. The session id is generated as hex and dashes, but the service name is the app's own. */
export function replayUrl(endpoint: string, service: string, sessionId: string): string {
	return `${endpoint}/v1/replay?service=${encodeURIComponent(service)}&session_id=${encodeURIComponent(sessionId)}`;
}
