/** The server refuses a batch larger than this after decompression, so a bigger one is not worth sending. */
export const MAX_CHUNK_BYTES = 10 * 1024 * 1024;

/**
 * Collects rrweb events until it is time to send them. Pure: the caller owns the timer. `take` returns the batch as
 * the JSON array text the server expects and empties the buffer.
 */
export class ChunkBuffer<E = unknown> {
	#events: E[] = [];

	constructor(private readonly maxEvents: number) {}

	get length(): number {
		return this.#events.length;
	}

	/** Adds an event; true when the buffer has reached its size limit and should be sent now. */
	push(event: E): boolean {
		this.#events.push(event);
		return this.#events.length >= this.maxEvents;
	}

	take(): string | null {
		if (this.#events.length === 0) return null;
		const json = JSON.stringify(this.#events);
		this.#events = [];
		return json;
	}
}

/** UTF-8 length of a string without allocating the bytes where `TextEncoder` is unavailable. */
export function byteLength(text: string): number {
	return typeof TextEncoder === 'undefined' ? text.length * 3 : new TextEncoder().encode(text).length;
}
