import assert from 'node:assert/strict';
import { gunzipSync } from 'node:zlib';
import { describe, it } from 'node:test';
import { ChunkBuffer, MAX_CHUNK_BYTES } from '../src/buffer.js';
import { replayUrl, resolveOptions } from '../src/options.js';
import { isSampled, resolveSessionId, type KeyValueStore } from '../src/session.js';
import { sendChunk } from '../src/transport.js';

function memoryStore(initial: Record<string, string> = {}): KeyValueStore & { data: Record<string, string> } {
	const data = { ...initial };
	return { data, getItem: (k) => data[k] ?? null, setItem: (k, v) => void (data[k] = v) };
}

const throwingStore: KeyValueStore = {
	getItem() {
		throw new Error('blocked');
	},
	setItem() {
		throw new Error('blocked');
	},
};

describe('ChunkBuffer', () => {
	it('signals when the event limit is reached and take() empties it', () => {
		const buffer = new ChunkBuffer<{ n: number }>(3);
		assert.equal(buffer.push({ n: 1 }), false);
		assert.equal(buffer.push({ n: 2 }), false);
		assert.equal(buffer.push({ n: 3 }), true);
		assert.equal(buffer.take(), '[{"n":1},{"n":2},{"n":3}]');
		assert.equal(buffer.length, 0);
		assert.equal(buffer.take(), null);
	});
});

describe('resolveOptions', () => {
	it('applies privacy-first defaults', () => {
		const o = resolveOptions({ endpoint: 'https://flare.example.com:4318/', service: 'web' });
		assert.equal(o.endpoint, 'https://flare.example.com:4318');
		assert.equal(o.maskAllInputs, true);
		assert.equal(o.maskAllText, false);
		assert.equal(o.blockSelector, '[data-flare-block]');
		assert.equal(o.maskTextSelector, '[data-flare-mask]');
		assert.equal(o.sampleRate, 1);
		assert.equal(o.flushIntervalMs, 5000);
		assert.equal(o.maxDurationMs, 3_600_000);
	});

	it('clamps unreasonable values', () => {
		const o = resolveOptions({ endpoint: 'x', service: 'web', sampleRate: 7, flushIntervalMs: 1, maxEventsPerChunk: 1, checkoutEveryMs: -5 });
		assert.deepEqual([o.sampleRate, o.flushIntervalMs, o.maxEventsPerChunk, o.checkoutEveryMs], [1, 1000, 10, 0]);
		assert.equal(resolveOptions({ endpoint: 'x', service: 'web', sampleRate: Number.NaN }).sampleRate, 1);
		assert.equal(resolveOptions({ endpoint: 'x', service: 'web', sampleRate: -1 }).sampleRate, 0);
	});

	it('requires an endpoint and a service', () => {
		assert.throws(() => resolveOptions({ endpoint: '', service: 'web' }), /endpoint/);
		assert.throws(() => resolveOptions({ endpoint: 'x', service: '' }), /service/);
	});

	it('encodes the service and session in the url', () => {
		assert.equal(replayUrl('https://f:4318', 'my web/app', 'abc-123'), 'https://f:4318/v1/replay?service=my%20web%2Fapp&session_id=abc-123');
	});
});

describe('session id', () => {
	it('prefers an explicit id and writes nothing', () => {
		const store = memoryStore();
		assert.equal(resolveSessionId('explicit-1234', store), 'explicit-1234');
		assert.deepEqual(store.data, {});
	});

	it('creates one and reuses it', () => {
		const store = memoryStore();
		let n = 0;
		const create = () => `generated-id-${++n}`;
		assert.equal(resolveSessionId(undefined, store, create), 'generated-id-1');
		assert.equal(resolveSessionId(undefined, store, create), 'generated-id-1');
		assert.equal(n, 1);
	});

	it('replaces a stored value the server would reject', () => {
		const store = memoryStore({ 'flare.session.id': 'bad id!' });
		assert.equal(resolveSessionId(undefined, store, () => 'fresh-session-1'), 'fresh-session-1');
	});

	it('works when storage is blocked', () => {
		assert.equal(resolveSessionId(undefined, throwingStore, () => 'fresh-session-2'), 'fresh-session-2');
		assert.equal(resolveSessionId(undefined, undefined, () => 'fresh-session-3'), 'fresh-session-3');
	});
});

describe('sampling', () => {
	it('is all or nothing at the ends and never touches storage', () => {
		const store = memoryStore();
		assert.equal(isSampled(1, store), true);
		assert.equal(isSampled(0, store), false);
		assert.deepEqual(store.data, {});
	});

	it('decides once and remembers', () => {
		const store = memoryStore();
		assert.equal(isSampled(0.5, store, () => 0.4), true);
		assert.equal(isSampled(0.5, store, () => 0.9), true);
		const other = memoryStore();
		assert.equal(isSampled(0.5, other, () => 0.9), false);
		assert.equal(isSampled(0.5, other, () => 0.1), false);
	});
});

describe('sendChunk', () => {
	const json = JSON.stringify([{ type: 3, timestamp: 1, data: {} }]);

	function fakeFetch(status: number, headers: Record<string, string> = {}) {
		const calls: { url: string; init: RequestInit }[] = [];
		const impl = (async (url: string, init: RequestInit) => {
			calls.push({ url, init });
			return { ok: status >= 200 && status < 300, status, headers: new Headers(headers) } as Response;
		}) as unknown as typeof fetch;
		return { calls, impl };
	}

	it('posts gzip json with the key', async () => {
		const { calls, impl } = fakeFetch(204);
		assert.equal(await sendChunk(json, { url: 'https://f/v1/replay', ingestKey: 'k', fetchImpl: impl }), 'sent');

		const { init } = calls[0];
		const headers = init.headers as Record<string, string>;
		assert.equal(headers.Authorization, 'Bearer k');
		assert.equal(headers['Content-Type'], 'application/json');
		assert.equal(headers['Content-Encoding'], 'gzip');
		assert.equal(init.keepalive, false);
		const body = Buffer.from(await (init.body as Blob).arrayBuffer());
		assert.equal(gunzipSync(body).toString(), json);
	});

	it('sends a small final batch uncompressed with keepalive', async () => {
		const { calls, impl } = fakeFetch(204);
		await sendChunk(json, { url: 'u', fetchImpl: impl }, true);

		const { init } = calls[0];
		assert.equal(init.body, json);
		assert.equal(init.keepalive, true);
		assert.equal((init.headers as Record<string, string>)['Content-Encoding'], undefined);
		assert.equal((init.headers as Record<string, string>).Authorization, undefined);
	});

	it('reports a rejected key, a failed request and an oversized batch', async () => {
		assert.equal(await sendChunk(json, { url: 'u', fetchImpl: fakeFetch(401).impl }), 'rejected');
		assert.equal(await sendChunk(json, { url: 'u', fetchImpl: fakeFetch(403).impl }), 'rejected');
		assert.equal(await sendChunk(json, { url: 'u', fetchImpl: fakeFetch(503).impl }), 'dropped');
		const failing = (async () => {
			throw new TypeError('network');
		}) as unknown as typeof fetch;
		assert.equal(await sendChunk(json, { url: 'u', fetchImpl: failing }), 'dropped');

		const { calls, impl } = fakeFetch(204);
		assert.equal(await sendChunk('x'.repeat(MAX_CHUNK_BYTES + 1), { url: 'u', fetchImpl: impl }), 'dropped');
		assert.equal(calls.length, 0);
	});

	it('answers a 429 with how long to hold off, from Retry-After or a default', async () => {
		assert.deepEqual(await sendChunk(json, { url: 'u', fetchImpl: fakeFetch(429, { 'Retry-After': '30' }).impl }), { retryAfterMs: 30_000 });
		assert.deepEqual(await sendChunk(json, { url: 'u', fetchImpl: fakeFetch(429).impl }), { retryAfterMs: 60_000 });
		assert.deepEqual(await sendChunk(json, { url: 'u', fetchImpl: fakeFetch(429, { 'Retry-After': '999999' }).impl }), { retryAfterMs: 3_600_000 });
	});
});

describe('onError options', () => {
	it('default to recording the whole session', () => {
		const o = resolveOptions({ endpoint: 'x', service: 'web' });
		assert.deepEqual([o.mode, o.bufferMs, o.afterErrorMs, o.captureErrors], ['session', 30_000, 10_000, true]);
	});

	it('clamp the buffer so a snapshot is not taken every instant', () => {
		const o = resolveOptions({ endpoint: 'x', service: 'web', mode: 'onError', bufferMs: 10, afterErrorMs: -1 });
		assert.deepEqual([o.mode, o.bufferMs, o.afterErrorMs], ['onError', 5000, 0]);
	});

	it('treat an unknown mode as session', () => {
		assert.equal(resolveOptions({ endpoint: 'x', service: 'web', mode: 'nope' as never }).mode, 'session');
	});
});
