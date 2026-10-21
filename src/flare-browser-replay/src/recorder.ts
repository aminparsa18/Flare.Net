import { record } from '@rrweb/record';
import { ChunkBuffer } from './buffer.js';
import { replayUrl, resolveOptions, type ReplayOptions } from './options.js';
import { isSampled, resolveSessionId, type KeyValueStore } from './session.js';
import { sendChunk } from './transport.js';

export interface ReplayHandle {
	/** The id the recording is stored under. Set it as the `session.id` span attribute to link spans and replay. */
	readonly sessionId: string;
	/** Sends what has been recorded so far. */
	flush(): Promise<void>;
	/** Stops recording and sends the rest. */
	stop(): Promise<void>;
}

const NOT_RECORDING: Omit<ReplayHandle, 'sessionId'> = { flush: async () => {}, stop: async () => {} };

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
	let stopped = false;
	let inFlight: Promise<void> = Promise.resolve();

	const send = (final: boolean): Promise<void> => {
		const json = buffer.take();
		if (json === null) return inFlight;
		inFlight = inFlight.then(async () => {
			const result = await sendChunk(json, { url, ingestKey: options.ingestKey }, final);
			if (result === 'rejected') halt('Flare rejected the ingest key or service.');
		});
		return inFlight;
	};

	const stopRecording = record({
		emit(event) {
			if (!stopped && buffer.push(event)) void send(false);
		},
		maskAllInputs: options.maskAllInputs,
		blockSelector: options.blockSelector,
		maskTextSelector: options.maskAllText ? '*' : options.maskTextSelector,
		blockClass: 'flare-block',
		maskTextClass: 'flare-mask',
		ignoreClass: 'flare-ignore',
		checkoutEveryNms: options.checkoutEveryMs > 0 ? options.checkoutEveryMs : undefined,
		sampling: { mousemove: 100, scroll: 200, input: 'last' },
	});

	const timer = setInterval(() => void send(false), options.flushIntervalMs);
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
		stopRecording?.();
		options.onStop?.(reason);
	}

	return {
		sessionId,
		flush: () => send(false),
		async stop() {
			if (stopped) return;
			stopped = true;
			clearInterval(timer);
			clearTimeout(limit);
			document.removeEventListener('visibilitychange', onHidden);
			window.removeEventListener('pagehide', onPageHide);
			stopRecording?.();
			await send(true);
		},
	};
}
