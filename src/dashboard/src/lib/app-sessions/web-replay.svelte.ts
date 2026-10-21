// Reactive state for the browser session replay player on /sessions/[sessionId] (ADR-0264). The chunk list is one small
// request; the events are fetched in parallel once the page asks for them, and rrweb's Replayer is imported only then,
// so a session without a recording costs the page nothing.

import { getWebReplay, getWebReplayChunk, type WebReplay } from '#lib/app-sessions-api.ts';
import {
	FULL_SNAPSHOT,
	LARGE_REPLAY_BYTES,
	extractConsole,
	extractNetwork,
	fitScale,
	mergeChunks,
	parseCanvasFrame,
	trimToFullSnapshot,
	type ConsoleEntry,
	type NetworkEntry,
	type ReplayEvent
} from './web-replay-events.ts';

/** Chunks fetched at once. */
const FETCH_CONCURRENCY = 4;

/** How long before the focus moment the recording opens, so the lead-up to an error is visible. */
const FOCUS_LEAD_MS = 5000;

export const REPLAY_SPEEDS = [1, 2, 4, 8] as const;

type ReplayerInstance = import('@rrweb/replay').Replayer;

export class WebReplayState {
	replay = $state.raw<WebReplay | null>(null);
	/** Chunks fetched so far while loading the events. */
	loadedChunks = $state(0);
	/** Loading the events (not the chunk list). */
	loading = $state(false);
	error = $state<string | null>(null);
	/** The Replayer is built and can play. */
	ready = $state(false);
	playing = $state(false);
	speed = $state<(typeof REPLAY_SPEEDS)[number]>(1);
	skipInactive = $state(true);
	/** Position and length of the recording, in ms. */
	elapsedMs = $state(0);
	totalMs = $state(0);
	/** Unix ms of the recording's first event, to relate a position to the timeline's spans. */
	startUnixMs = $state(0);
	/** Scale the recorded page is drawn at, to fit the container. */
	scale = $state(1);
	/** Size of the recorded page, from rrweb's resize event; 0 until the first one. */
	viewportWidth = $state(0);
	viewportHeight = $state(0);

	/** Console calls and requests the recorder captured (ADR-0266); empty when it was not asked to. */
	consoleEntries = $state.raw<ConsoleEntry[]>([]);
	networkEntries = $state.raw<NetworkEntry[]>([]);

	#abort: AbortController | null = null;
	#replayer: ReplayerInstance | null = null;
	#raf = 0;
	#sessionId = '';
	#focusUnixMs: number | undefined;
	#measure: HTMLElement | null = null;
	/**
	 * The earliest position the Replayer can show: one millisecond past the first full snapshot. It applies an event
	 * straight away only when the event is strictly older than the position asked for, so at the snapshot's own time (or
	 * 0, when the snapshot is the first event) it keeps showing whatever page it had before.
	 */
	#firstFrameMs = 0;
	#resizeObserver: ResizeObserver | null = null;

	get hasReplay(): boolean {
		return (this.replay?.chunks.length ?? 0) > 0;
	}

	get totalBytes(): number {
		return this.replay?.chunks.reduce((sum, c) => sum + c.payloadBytes, 0) ?? 0;
	}

	get isLarge(): boolean {
		return this.totalBytes > LARGE_REPLAY_BYTES;
	}

	/** Unix ms of the current position. */
	get currentUnixMs(): number {
		return this.startUnixMs + this.elapsedMs;
	}

	/** `focusUnixMs`: where the recording opens once it is loaded, a few seconds before it (an error's time). */
	async load(sessionId: string, fromUnixMs?: number, toUnixMs?: number, focusUnixMs?: number): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;
		this.#sessionId = sessionId;
		this.#focusUnixMs = focusUnixMs;
		try {
			const replay = await getWebReplay(sessionId, { fromUnixMs, toUnixMs }, abort.signal);
			if (!abort.signal.aborted) this.replay = replay;
		} catch (err) {
			if (!abort.signal.aborted) this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/**
	 * Fetches every chunk and builds the Replayer inside `root`. `measure` is the element whose width the recorded page is
	 * scaled to fit (the root itself is sized to the recorded page). Call once, after the user asks to watch.
	 */
	async start(root: HTMLElement, measure: HTMLElement): Promise<void> {
		const replay = this.replay;
		if (!replay || this.loading || this.ready) return;
		const abort = this.#abort ?? new AbortController();
		this.loading = true;
		this.error = null;
		this.loadedChunks = 0;
		this.#measure = measure;
		try {
			const chunks = await this.#fetchAll(replay, abort.signal);
			if (abort.signal.aborted) return;
			const events = trimToFullSnapshot(mergeChunks(chunks));
			if (events.length < 2) {
				this.error = 'This recording has no page snapshot to start from.';
				return;
			}
			this.consoleEntries = extractConsole(events);
			this.networkEntries = extractNetwork(events);
			const [{ Replayer }] = await Promise.all([import('@rrweb/replay'), import('@rrweb/replay/dist/style.css')]);
			if (abort.signal.aborted) return;
			this.#build(Replayer, events, root, measure);
		} catch (err) {
			if (!abort.signal.aborted) this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	play(): void {
		if (!this.#replayer) return;
		// At the end, play() from the start rather than doing nothing.
		this.#replayer.play(this.elapsedMs >= this.totalMs ? this.#firstFrameMs : Math.max(this.#firstFrameMs, this.elapsedMs));
		this.playing = true;
		this.#tick();
	}

	pause(): void {
		if (!this.#replayer) return;
		this.#replayer.pause();
		this.playing = false;
		cancelAnimationFrame(this.#raf);
		this.elapsedMs = this.#replayer.getCurrentTime();
	}

	toggle(): void {
		if (this.playing) this.pause();
		else this.play();
	}

	seek(ms: number): void {
		if (!this.#replayer) return;
		// Earlier than the first snapshot there is no page to draw, and the Replayer would keep showing the old one.
		const at = Math.min(this.totalMs, Math.max(0, ms));
		const target = Math.max(this.#firstFrameMs, at);
		if (this.playing) this.#replayer.play(target);
		else this.#replayer.pause(target);
		this.elapsedMs = at;
	}

	/** Moves to a Unix-ms time (a span's start), clamped to the recording. */
	seekToUnixMs(unixMs: number): void {
		this.seek(unixMs - this.startUnixMs);
	}

	setSpeed(speed: (typeof REPLAY_SPEEDS)[number]): void {
		this.speed = speed;
		this.#replayer?.setConfig({ speed });
	}

	setSkipInactive(skip: boolean): void {
		this.skipInactive = skip;
		this.#replayer?.setConfig({ skipInactive: skip });
	}

	dispose(): void {
		this.#abort?.abort();
		cancelAnimationFrame(this.#raf);
		this.#resizeObserver?.disconnect();
		this.#resizeObserver = null;
		this.#replayer?.destroy();
		this.#replayer = null;
		this.ready = false;
		this.playing = false;
	}

	async #fetchAll(replay: WebReplay, signal: AbortSignal): Promise<(unknown[] | null)[]> {
		const results: (unknown[] | null)[] = new Array(replay.chunks.length).fill(null);
		let next = 0;
		const worker = async () => {
			while (next < replay.chunks.length && !signal.aborted) {
				const i = next++;
				results[i] = await getWebReplayChunk(this.#sessionId, replay.chunks[i], signal);
				this.loadedChunks++;
			}
		};
		await Promise.all(Array.from({ length: Math.min(FETCH_CONCURRENCY, replay.chunks.length) }, worker));
		return results;
	}

	#build(Replayer: typeof import('@rrweb/replay').Replayer, events: ReplayEvent[], root: HTMLElement, measure: HTMLElement): void {
		root.replaceChildren();
		const replayer = new Replayer(events as ConstructorParameters<typeof Replayer>[0], {
			root,
			speed: this.speed,
			skipInactive: this.skipInactive,
			showWarning: false,
			mouseTail: { strokeStyle: '#f97316' }
		});
		const meta = replayer.getMetaData();
		this.startUnixMs = meta.startTime;
		this.totalMs = meta.totalTime;
		this.#firstFrameMs = Math.max(0, (events.find((e) => e.type === FULL_SNAPSHOT)?.timestamp ?? events[0].timestamp) - events[0].timestamp) + 1;
		this.elapsedMs = 0;
		replayer.on('resize', (dimension: unknown) => {
			const { width, height } = dimension as { width: number; height: number };
			this.viewportWidth = width;
			this.viewportHeight = height;
			this.#fit();
		});
		// Canvas pixels are not part of the DOM snapshot: the recorder sends them as frames, painted onto the replayed canvas
		// here rather than by rrweb's own canvas replay, which would need scripts enabled in the replay frame (ADR-0267).
		replayer.on('custom-event', (event: unknown) => {
			const frame = parseCanvasFrame(event as ReplayEvent);
			if (frame) this.#paintCanvas(replayer, frame.id, frame.src);
		});
		replayer.on('finish', () => {
			this.playing = false;
			cancelAnimationFrame(this.#raf);
			this.elapsedMs = this.totalMs;
		});
		this.#resizeObserver = new ResizeObserver(() => this.#fit());
		this.#resizeObserver.observe(measure);
		this.#replayer = replayer;
		this.ready = true;
		// Show the first frame, or the moment the page was opened for, without starting playback.
		replayer.pause(this.#firstFrameMs);
		if (this.#focusUnixMs !== undefined) this.seekToUnixMs(this.#focusUnixMs - FOCUS_LEAD_MS);
	}

	/** Latest picture asked for per canvas, so a slow decode of an old frame never overwrites a newer one. */
	#canvasSeq = new Map<number, number>();

	#paintCanvas(replayer: ReplayerInstance, id: number, src: string): void {
		const seq = (this.#canvasSeq.get(id) ?? 0) + 1;
		this.#canvasSeq.set(id, seq);
		const image = new Image();
		image.onload = () => {
			if (this.#canvasSeq.get(id) !== seq || this.#replayer !== replayer) return;
			const node = replayer.getMirror().getNode(id);
			// The element belongs to the replay frame's window, so `instanceof HTMLCanvasElement` would be false.
			if (node?.nodeName !== 'CANVAS') return;
			const canvas = node as HTMLCanvasElement;
			canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
		};
		image.src = src;
	}

	#fit(): void {
		if (this.#measure && this.viewportWidth > 0) this.scale = fitScale(this.#measure.clientWidth, this.viewportWidth);
	}

	#tick(): void {
		cancelAnimationFrame(this.#raf);
		const step = () => {
			if (!this.playing || !this.#replayer) return;
			this.elapsedMs = Math.min(this.totalMs, this.#replayer.getCurrentTime());
			this.#raf = requestAnimationFrame(step);
		};
		this.#raf = requestAnimationFrame(step);
	}
}
