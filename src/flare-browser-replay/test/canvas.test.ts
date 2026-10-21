import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { FrameGate, frameSize, MAX_FRAME_SIDE } from '../src/canvas.js';
import { resolveOptions } from '../src/options.js';

describe('FrameGate', () => {
	it('skips a canvas that has not changed and sends it again once it does', () => {
		const gate = new FrameGate();
		assert.equal(gate.accept(1, 'a'), true);
		assert.equal(gate.accept(1, 'a'), false);
		assert.equal(gate.accept(2, 'a'), true);
		assert.equal(gate.accept(1, 'b'), true);
	});

	it('sends every canvas again after invalidate', () => {
		const gate = new FrameGate();
		gate.accept(1, 'a');
		gate.invalidate();
		assert.equal(gate.accept(1, 'a'), true);
	});

	it('drops a frame over the size limit without remembering it', () => {
		const gate = new FrameGate(5, 100);
		assert.equal(gate.accept(1, '123456'), false);
		assert.equal(gate.accept(1, '12345'), true);
	});

	it('stops taking frames once the session budget is spent', () => {
		const gate = new FrameGate(10, 12);
		assert.equal(gate.accept(1, '123456'), true);
		assert.equal(gate.accept(2, '123456'), true);
		assert.equal(gate.exhausted, true);
		assert.equal(gate.accept(3, 'x'), false);
	});
});

describe('frameSize', () => {
	it('keeps the shape and never enlarges', () => {
		assert.deepEqual(frameSize(400, 200), { w: 400, h: 200 });
		assert.deepEqual(frameSize(1600, 800), { w: MAX_FRAME_SIDE, h: MAX_FRAME_SIDE / 2 });
		assert.deepEqual(frameSize(300, 3000), { w: 80, h: MAX_FRAME_SIDE });
	});

	it('never returns a zero side', () => {
		assert.deepEqual(frameSize(1, 100_000), { w: 1, h: MAX_FRAME_SIDE });
	});
});

describe('captureCanvas option', () => {
	const fps = (captureCanvas: unknown, extra = {}) =>
		resolveOptions({ endpoint: 'x', service: 'web', captureCanvas: captureCanvas as boolean | number, ...extra }).canvasFps;

	it('is off by default and for junk', () => {
		assert.equal(fps(undefined), 0);
		assert.equal(fps(false), 0);
		assert.equal(fps(0), 0);
		assert.equal(fps(-3), 0);
		assert.equal(fps(Number.NaN), 0);
		assert.equal(fps('yes'), 0);
	});

	it('true is one frame per second and a number is clamped to 1..10', () => {
		assert.equal(fps(true), 1);
		assert.equal(fps(0.2), 1);
		assert.equal(fps(4), 4);
		assert.equal(fps(60), 10);
	});

	it('is off when all text is masked', () => {
		assert.equal(fps(true, { maskAllText: true }), 0);
	});
});
