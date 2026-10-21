/** rrweb event types the buffer cares about: `Meta` opens every page snapshot (the first one and each checkout). */
const META = 4;

/** Most events held while waiting for an error. Past it the oldest whole segment goes, if there is more than one. */
const DEFAULT_MAX_BUFFERED = 20_000;

export interface RollingEvent {
	type: number;
}

type Phase = 'buffering' | 'live' | 'waiting';

/**
 * The "record only around an error" mode. While nothing has gone wrong it keeps the last two segments of the recording in memory,
 * where a segment is everything from one page snapshot (rrweb's `Meta` event, taken at the start and at every `checkoutEveryNms`)
 * up to the next. Older segments are dropped, so what is held reaches back between one and two checkout intervals and always
 * starts at a snapshot the player can draw from. `trigger` hands those events out (to be sent) and switches to passing events
 * straight through for `afterMs`; then it discards events until the next snapshot, since incremental events without the snapshot
 * they build on cannot be replayed, and goes back to buffering. Pure: the caller supplies the clock.
 */
export class RollingBuffer<E extends RollingEvent = RollingEvent> {
	#segments: E[][] = [];
	#buffered = 0;
	#phase: Phase = 'buffering';
	#liveUntil = 0;
	/** An error arrived while waiting for a snapshot. */
	#armed = false;

	constructor(
		private readonly afterMs: number,
		private readonly maxBuffered = DEFAULT_MAX_BUFFERED,
	) {}

	/** Events currently held, waiting for an error. */
	get length(): number {
		return this.#buffered;
	}

	get live(): boolean {
		return this.#phase === 'live';
	}

	/** Takes one recorded event; returns the events to send now (the event itself while live, nothing otherwise). */
	push(event: E, now: number): E[] {
		this.tick(now);
		switch (this.#phase) {
			case 'live':
				return [event];
			case 'waiting':
				if (event.type !== META) return [];
				// An error that came while no snapshot was available starts its recording at the next one.
				if (this.#armed) {
					this.#armed = false;
					this.#phase = 'live';
					this.#liveUntil = now + this.afterMs;
					return [event];
				}
				this.#phase = 'buffering';
				this.#add(event, true);
				return [];
			default:
				this.#add(event, event.type === META);
				return [];
		}
	}

	/** Ends the live window once it has run its time. Called on every event and by the caller's timer, so a quiet page still ends it. */
	tick(now: number): void {
		if (this.#phase === 'live' && now >= this.#liveUntil) this.#phase = 'waiting';
	}

	/**
	 * An error happened. Returns what was buffered (oldest first) when this starts a recording, nothing when one is already
	 * running, in which case the live window is extended instead. Between two recordings (waiting for a snapshot) there is nothing
	 * to return; the next snapshot then starts one.
	 */
	trigger(now: number): E[] {
		this.tick(now);
		const until = now + this.afterMs;
		if (this.#phase === 'live') {
			this.#liveUntil = Math.max(this.#liveUntil, until);
			return [];
		}
		if (this.#phase === 'waiting') {
			this.#armed = true;
			return [];
		}
		const held = this.#segments.flat();
		this.#segments = [];
		this.#buffered = 0;
		this.#liveUntil = until;
		this.#phase = 'live';
		return held;
	}

	#add(event: E, startsSegment: boolean): void {
		if (startsSegment || this.#segments.length === 0) {
			this.#segments.push([event]);
			// Two segments: the one that is filling and the one before it, which still reaches back a full interval.
			while (this.#segments.length > 2) this.#drop();
		} else {
			this.#segments[this.#segments.length - 1].push(event);
		}
		this.#buffered++;
		while (this.#buffered > this.maxBuffered && this.#segments.length > 1) this.#drop();
	}

	#drop(): void {
		const dropped = this.#segments.shift();
		this.#buffered -= dropped?.length ?? 0;
	}
}
