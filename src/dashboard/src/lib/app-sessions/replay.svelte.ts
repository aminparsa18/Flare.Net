// Reactive state for the session replay player on /sessions/[sessionId] - a session's scheduled screenshots
// (ADR-0192). The list of frame times is one small request; each image is fetched when it is first shown, and the
// next one is prefetched while playing.

import { getAppSessionReplay, getAppSessionReplayFrame, type AppSessionReplay } from '#lib/app-sessions-api.ts';

/** Playback speed: one frame per second, whatever the capture interval was. */
export const REPLAY_FRAME_MS = 1_000;

export class AppSessionReplayState {
	replay = $state.raw<AppSessionReplay | null>(null);
	index = $state(0);
	playing = $state(false);
	error = $state<string | null>(null);
	/** Data URLs by frame time; null = looked up, none found. */
	frames = $state.raw<Record<number, string | null>>({});

	#abort: AbortController | null = null;
	#timer: ReturnType<typeof setInterval> | null = null;
	#inflight = new Set<number>();

	get times(): number[] {
		return this.replay?.frameUnixMs ?? [];
	}

	get currentTime(): number | undefined {
		return this.times[this.index];
	}

	get currentUrl(): string | null | undefined {
		const t = this.currentTime;
		return t === undefined ? undefined : this.frames[t];
	}

	async load(sessionId: string, fromUnixMs?: number, toUnixMs?: number): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;
		this.sessionId = sessionId;
		try {
			const replay = await getAppSessionReplay(sessionId, { fromUnixMs, toUnixMs }, abort.signal);
			if (abort.signal.aborted) return;
			this.replay = replay;
			this.index = 0;
			void this.#ensure(0);
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	sessionId = '';

	select(i: number): void {
		const last = this.times.length - 1;
		if (last < 0) return;
		this.index = Math.min(last, Math.max(0, i));
		void this.#ensure(this.index);
		void this.#ensure(this.index + 1);
	}

	toggle(): void {
		if (this.playing) return this.#stop();
		if (this.times.length === 0) return;
		if (this.index >= this.times.length - 1) this.select(0);
		this.playing = true;
		this.#timer = setInterval(() => {
			if (this.index >= this.times.length - 1) return this.#stop();
			this.select(this.index + 1);
		}, REPLAY_FRAME_MS);
	}

	dispose(): void {
		this.#abort?.abort();
		this.#stop();
	}

	#stop(): void {
		if (this.#timer) clearInterval(this.#timer);
		this.#timer = null;
		this.playing = false;
	}

	async #ensure(i: number): Promise<void> {
		const t = this.times[i];
		if (t === undefined || t in this.frames || this.#inflight.has(t)) return;
		this.#inflight.add(t);
		try {
			const shot = await getAppSessionReplayFrame(this.sessionId, t, this.#abort?.signal);
			this.frames = { ...this.frames, [t]: shot ? `data:${shot.contentType};base64,${shot.imageBase64}` : null };
		} catch (err) {
			if (!this.#abort?.signal.aborted) this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.#inflight.delete(t);
		}
	}
}

/** The last event that started at or before `timeMs`, so a frame can say what the app was doing. */
export function eventAt<T extends { startUnixMs: number }>(events: T[], timeMs: number): T | undefined {
	let found: T | undefined;
	for (const e of events) {
		if (e.startUnixMs > timeMs) break;
		found = e;
	}
	return found;
}
