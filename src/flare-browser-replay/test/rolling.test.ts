import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { RollingBuffer } from '../src/rolling.js';

const META = 4;
const FULL = 2;
const INC = 3;

/** A labelled event, so assertions read as the sequence that was recorded. */
const ev = (type: number, label: string) => ({ type, label });
const labels = (events: { label?: string }[]) => events.map((e) => e.label);

/** What rrweb emits at the start and at each checkout. */
function snapshot(buffer: RollingBuffer<{ type: number; label: string }>, at: number, n: number) {
	return [buffer.push(ev(META, `m${n}`), at), buffer.push(ev(FULL, `f${n}`), at)];
}

describe('RollingBuffer', () => {
	it('sends nothing while it is only buffering', () => {
		const b = new RollingBuffer<{ type: number; label: string }>(10_000);
		snapshot(b, 0, 1);
		assert.deepEqual(b.push(ev(INC, 'a'), 100), []);
		assert.equal(b.length, 3);
		assert.equal(b.live, false);
	});

	it('keeps the filling segment and the one before it, each starting at a snapshot', () => {
		const b = new RollingBuffer<{ type: number; label: string }>(10_000);
		snapshot(b, 0, 1);
		b.push(ev(INC, 'a'), 1);
		snapshot(b, 30_000, 2);
		b.push(ev(INC, 'b'), 30_001);
		snapshot(b, 60_000, 3);
		b.push(ev(INC, 'c'), 60_001);

		assert.deepEqual(labels(b.trigger(60_002)), ['m2', 'f2', 'b', 'm3', 'f3', 'c']);
	});

	it('hands out everything buffered on the first error, then passes events straight through', () => {
		const b = new RollingBuffer<{ type: number; label: string }>(10_000);
		snapshot(b, 0, 1);
		b.push(ev(INC, 'a'), 5);
		assert.deepEqual(labels(b.trigger(10)), ['m1', 'f1', 'a']);
		assert.equal(b.live, true);
		assert.equal(b.length, 0);
		assert.deepEqual(labels(b.push(ev(INC, 'after'), 20)), ['after']);
	});

	it('extends the live window when a second error comes during it, without sending the past twice', () => {
		const b = new RollingBuffer<{ type: number; label: string }>(10_000);
		snapshot(b, 0, 1);
		b.trigger(100);
		assert.deepEqual(b.trigger(8_000), []);
		assert.deepEqual(labels(b.push(ev(INC, 'x'), 17_000)), ['x']); // 8 000 + 10 000 > 17 000
		assert.equal(b.live, true);
	});

	it('stops after the window and discards events until the next snapshot', () => {
		const b = new RollingBuffer<{ type: number; label: string }>(10_000);
		snapshot(b, 0, 1);
		b.trigger(100);
		assert.deepEqual(labels(b.push(ev(INC, 'in'), 5_000)), ['in']);
		assert.deepEqual(b.push(ev(INC, 'gap'), 10_100), []);
		assert.equal(b.live, false);
		assert.equal(b.length, 0, 'incremental events with no snapshot to build on are dropped');

		snapshot(b, 30_000, 2);
		b.push(ev(INC, 'later'), 30_001);
		assert.deepEqual(labels(b.trigger(30_002)), ['m2', 'f2', 'later']);
	});

	it('ends the window from the timer when the page goes quiet', () => {
		const b = new RollingBuffer<{ type: number; label: string }>(10_000);
		snapshot(b, 0, 1);
		b.trigger(0);
		b.tick(10_000);
		assert.equal(b.live, false);
	});

	it('starts the next recording at the next snapshot when an error arrives between two', () => {
		const b = new RollingBuffer<{ type: number; label: string }>(1_000);
		snapshot(b, 0, 1);
		b.trigger(0);
		b.tick(1_000); // waiting for a snapshot
		assert.deepEqual(b.trigger(5_000), []);
		assert.deepEqual(b.push(ev(INC, 'ignored'), 6_000), []);
		assert.deepEqual(labels(b.push(ev(META, 'm2'), 30_000)), ['m2']);
		assert.deepEqual(labels(b.push(ev(FULL, 'f2'), 30_000)), ['f2']);
		assert.equal(b.live, true);
	});

	it('drops the oldest whole segment when too many events pile up, but never the only one', () => {
		const b = new RollingBuffer<{ type: number; label: string }>(1_000, 5);
		snapshot(b, 0, 1);
		for (let i = 0; i < 10; i++) b.push(ev(INC, `a${i}`), i);
		assert.equal(b.length, 12, 'a single segment is kept whole, however big');

		snapshot(b, 30_000, 2);
		assert.equal(b.length, 2, 'the new snapshot pushes the over-size segment out');
		assert.deepEqual(labels(b.trigger(30_001)), ['m2', 'f2']);
	});
});
