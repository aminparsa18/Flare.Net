import { MAX_CHUNK_BYTES, byteLength } from './buffer.js';

/** Browsers cap the body of a `keepalive` request at 64 KiB in total. */
const KEEPALIVE_LIMIT = 60 * 1024;

/** `throttled`: the key is over a limit (HTTP 429); sending should pause for `retryAfterMs`. */
export type SendResult = 'sent' | 'dropped' | 'rejected' | { retryAfterMs: number };

const DEFAULT_RETRY_MS = 60_000;
const MAX_RETRY_MS = 3_600_000;

export interface TransportOptions {
	url: string;
	ingestKey?: string;
	fetchImpl?: typeof fetch;
}

async function gzip(text: string): Promise<Blob | null> {
	if (typeof CompressionStream === 'undefined' || typeof Blob === 'undefined') return null;
	try {
		return await new Response(new Blob([text]).stream().pipeThrough(new CompressionStream('gzip'))).blob();
	} catch {
		return null;
	}
}

/**
 * Posts one batch. Best effort: a network failure or a server error drops the batch (the recorder does not retry, so a
 * replay may have a gap, not duplicates). `rejected` means the server will not accept this recording at all (a bad or
 * restricted key, a service outside the key's list), so the caller should stop recording. A 429 (the key's byte limit,
 * ADR-0051) answers with how long to hold off; batches in that time are not sent. With `final`, a small batch goes
 * out uncompressed straight away, because on page hide there is no time to wait for gzip.
 */
export async function sendChunk(json: string, options: TransportOptions, final = false): Promise<SendResult> {
	const bytes = byteLength(json);
	if (bytes > MAX_CHUNK_BYTES) return 'dropped';
	const doFetch = options.fetchImpl ?? fetch;
	const headers: Record<string, string> = { 'Content-Type': 'application/json' };
	if (options.ingestKey) headers.Authorization = `Bearer ${options.ingestKey}`;

	let body: BodyInit = json;
	if (!(final && bytes <= KEEPALIVE_LIMIT)) {
		const compressed = await gzip(json);
		if (compressed) {
			body = compressed;
			headers['Content-Encoding'] = 'gzip';
		}
	}
	try {
		const response = await doFetch(options.url, { method: 'POST', headers, body, keepalive: final && (body instanceof Blob ? body.size : bytes) <= KEEPALIVE_LIMIT });
		if (response.status === 401 || response.status === 403) return 'rejected';
		if (response.status === 429) {
			const seconds = Number(response.headers?.get('Retry-After'));
			return { retryAfterMs: Number.isFinite(seconds) && seconds > 0 ? Math.min(MAX_RETRY_MS, seconds * 1000) : DEFAULT_RETRY_MS };
		}
		return response.ok ? 'sent' : 'dropped';
	} catch {
		return 'dropped';
	}
}
