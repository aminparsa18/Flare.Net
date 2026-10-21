/** The custom event tag a canvas frame is recorded under. The dashboard paints these onto the replayed canvas (ADR-0267). */
export const CANVAS_TAG = 'flare-canvas';

/** Longest side of a recorded frame, in pixels. A bigger canvas is scaled down; the replay stretches it back to size. */
export const MAX_FRAME_SIDE = 800;
/** A frame whose encoded form is longer than this is dropped: it would eat the session's byte cap on its own. */
export const MAX_FRAME_CHARS = 150_000;
/** Total encoded frame text a session records before canvas capture stops (about 25 MB). */
export const MAX_SESSION_CHARS = 25_000_000;

export interface CanvasFrame {
	/** The rrweb node id of the canvas element. */
	id: number;
	/** The size the frame was scaled to. */
	w: number;
	h: number;
	/** `data:image/webp;base64,...` (or png where the browser cannot encode webp). */
	src: string;
}

/**
 * Decides which frames are worth sending: a canvas that has not changed since its last frame is skipped, a frame that is
 * too large is skipped, and once the session has sent its budget nothing more is taken. Pure so it can be tested without a DOM.
 */
export class FrameGate {
	readonly #last = new Map<number, string>();
	#spent = 0;

	constructor(
		private readonly maxFrameChars = MAX_FRAME_CHARS,
		private readonly maxSessionChars = MAX_SESSION_CHARS,
	) {}

	/** True once the session budget is used up; the caller stops taking frames. */
	get exhausted(): boolean {
		return this.#spent >= this.maxSessionChars;
	}

	/** Whether to send this frame. Records it as the canvas's latest when it is sent. */
	accept(id: number, src: string): boolean {
		if (this.exhausted || src.length > this.maxFrameChars || this.#last.get(id) === src) return false;
		this.#last.set(id, src);
		this.#spent += src.length;
		return true;
	}

	/** Forget what each canvas last showed, so the next pass sends every canvas again (after a new page snapshot). */
	invalidate(): void {
		this.#last.clear();
	}
}

/** The size a canvas is scaled to, keeping its shape; never enlarged. */
export function frameSize(width: number, height: number, maxSide = MAX_FRAME_SIDE): { w: number; h: number } {
	const scale = Math.min(1, maxSide / Math.max(width, height));
	return { w: Math.max(1, Math.round(width * scale)), h: Math.max(1, Math.round(height * scale)) };
}

export interface CanvasCaptureOptions {
	fps: number;
	/** Canvases inside an element matching either selector are never recorded: a blocked or masked area stays blank. */
	skipSelectors: readonly string[];
	/** The rrweb node id of an element, or -1 when rrweb has not recorded it. */
	getId: (el: Node) => number;
	emit: (frame: CanvasFrame) => void;
}

export interface CanvasCapture {
	/** Takes a frame of every canvas now. Called on a timer; exposed for a test and for `invalidate`. */
	pass(): void;
	/** Resend every canvas on the next pass; call after a full page snapshot. */
	invalidate(): void;
	stop(): void;
}

/**
 * Snapshots `<canvas>` elements a few times a second and hands each changed one to `emit`. The pixels are read with
 * `drawImage` onto a scratch canvas, which also works for WebGL while `toDataURL` on it usually returns blank. A canvas that
 * is tainted by a cross-origin image cannot be read; it is skipped.
 */
export function startCanvasCapture(options: CanvasCaptureOptions): CanvasCapture {
	const gate = new FrameGate();
	const scratch = document.createElement('canvas');
	const skip = options.skipSelectors.filter(Boolean).join(',');
	let encodeType = 'image/webp';

	const encode = (canvas: HTMLCanvasElement, w: number, h: number): string | undefined => {
		scratch.width = w;
		scratch.height = h;
		const ctx = scratch.getContext('2d');
		if (!ctx) return undefined;
		ctx.clearRect(0, 0, w, h);
		ctx.drawImage(canvas, 0, 0, w, h);
		const src = scratch.toDataURL(encodeType, 0.5);
		// A browser that cannot encode webp silently returns png; remember, so the next frame skips the guess.
		if (encodeType === 'image/webp' && !src.startsWith('data:image/webp')) encodeType = 'image/png';
		return src;
	};

	const pass = (): void => {
		if (gate.exhausted || document.visibilityState === 'hidden') return;
		for (const canvas of Array.from(document.getElementsByTagName('canvas'))) {
			if (canvas.width === 0 || canvas.height === 0) continue;
			if (skip && (canvas.matches(skip) || canvas.closest(skip))) continue;
			const id = options.getId(canvas);
			if (id < 0) continue;
			const { w, h } = frameSize(canvas.width, canvas.height);
			try {
				const src = encode(canvas, w, h);
				if (src && gate.accept(id, src)) options.emit({ id, w, h, src });
			} catch {
				// Tainted or lost context: nothing to record for this canvas.
			}
		}
	};

	// Reading a WebGL canvas only works in the frame it was drawn, so the pass waits for the browser's next paint.
	const timer = setInterval(() => requestAnimationFrame(pass), Math.round(1000 / options.fps));
	return {
		pass,
		invalidate: () => gate.invalidate(),
		stop: () => clearInterval(timer),
	};
}
