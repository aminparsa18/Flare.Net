import { record } from '@rrweb/record';
import { ChunkBuffer } from './buffer.js';
import { RollingBuffer } from './rolling.js';
import { replayUrl, resolveOptions, type ReplayOptions } from './options.js';
import { isSampled, resolveSessionId, type KeyValueStore } from './session.js';
import { sendChunk } from './transport.js';

export interface ReplayHandle {
	/** The id the recording is stored under. Set it as the `session.id` span attribute to link spans and replay. */
	readonly sessionId: string;
	/**
	 * `onError` mode: keeps the seconds before now and the seconds after, sending them. Call it where you report an error you
	 * handled yourself; uncaught errors do it on their own. A no-op in `session` mode.
	 */
	keep(): void;
	/** Sends what has been recorded so far. */
	flush(): Promise<void>;
	/** Stops recording and sends the rest. */
	stop(): Promise<void>;
}

const NOT_RECORDING: Omit<ReplayHandle, 'sessionId'> = { keep: () => {}, flush: async () => {}, stop: async () => {} };

function sessionStore(): KeyValueStore | undefined {
	try {
		return typeof sessionStorage === 'undefined' ? undefined : sessionStorage;
	} catch {
		return undefined;
	}
}

/**
 * Starts recording the page. Returns at once; batches are sent every few seconds and when the tab is hidden. A no-op outside
 * a browser, and for a session the sample rate excludes (the handle still carries the session id).
 */
export function startReplay(input: ReplayOptions): ReplayHandle {
	const options = resolveOptions(input);
	const store = sessionStore();
	const sessionId = resolveSessionId(options.sessionId, store);
	if (typeof window === 'undefined' || typeof document === 'undefined' || !isSampled(options.sampleRate, store)) {
		return { sessionId, ...NOT_RECORDING };
	}

	const url = replayUrl(options.endpoint, options.service, sessionId);
	const buffer = new ChunkBuffer(options.maxEventsPerChunk);
	const rolling = options.mode === 'onError' ? new RollingBuffer<{ type: number }>(options.afterErrorMs) : undefined;
	let throttledUntil = 0;
	let stopped = false;
	let inFlight: Promise<void> = Promise.resolve();

	const send = (final: boolean): Promise<void> => {
		const json = buffer.take();
		if (json === null) return inFlight;
		inFlight = inFlight.then(async () => {
			if (Date.now() < throttledUntil) return;
			const result = await sendChunk(json, { url, ingestKey: options.ingestKey }, final);
			if (result === 'rejected') halt('Flare rejected the ingest key or service.');
			else if (typeof result === 'object') throttledUntil = Date.now() + result.retryAfterMs;
		});
		return inFlight;
	};

	const stopRecording = record({
		emit(event) {
			if (stopped) return;
			// In onError mode the rolling buffer holds events back until an error asks for them.
			const out = rolling ? rolling.push(event, Date.now()) : [event];
			let full = false;
			for (const e of out) full = buffer.push(e) || full;
			if (full) void send(false);
		},
		maskAllInputs: options.maskAllInputs,
		blockSelector: options.blockSelector,
		maskTextSelector: options.maskAllText ? '*' : options.maskTextSelector,
		blockClass: 'flare-block',
		maskTextClass: 'flare-mask',
		ignoreClass: 'flare-ignore',
		checkoutEveryNms: rolling ? options.bufferMs : options.checkoutEveryMs > 0 ? options.checkoutEveryMs : undefined,
		sampling: { mousemove: 100, scroll: 200, input: 'last' },
	});

	const keep = (): void => {
		if (!rolling || stopped) return;
		for (const e of rolling.trigger(Date.now())) buffer.push(e);
		void send(false);
	};
	const timer = setInterval(() => {
		rolling?.tick(Date.now());
		void send(false);
	}, options.flushIntervalMs);
	const onError = () => keep();
	if (rolling && options.captureErrors) {
		window.addEventListener('error', onError);
		window.addEventListener('unhandledrejection', onError);
	}
	const limit = setTimeout(() => halt('Reached maxDurationMs.'), options.maxDurationMs);
	const onHidden = () => {
		if (document.visibilityState === 'hidden') void send(true);
	};
	const onPageHide = () => void send(true);
	document.addEventListener('visibilitychange', onHidden);
	window.addEventListener('pagehide', onPageHide);

	function halt(reason: string): void {
		if (stopped) return;
		stopped = true;
		clearInterval(timer);
		clearTimeout(limit);
		document.removeEventListener('visibilitychange', onHidden);
		window.removeEventListener('pagehide', onPageHide);
		window.removeEventListener('error', onError);
		window.removeEventListener('unhandledrejection', onError);
		stopRecording?.();
		options.onStop?.(reason);
	}

	return {
		sessionId,
		keep,
		flush: () => send(false),
		async stop() {
			if (stopped) return;
			stopped = true;
			clearInterval(timer);
			clearTimeout(limit);
			document.removeEventListener('visibilitychange', onHidden);
			window.removeEventListener('pagehide', onPageHide);
			window.removeEventListener('error', onError);
			window.removeEventListener('unhandledrejection', onError);
			stopRecording?.();
			await send(true);
		},
	};
}
