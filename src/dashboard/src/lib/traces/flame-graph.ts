// Layout for the trace flame graph: every span becomes one bar on the trace-wide time
// axis, stacked beneath its parent. Unlike a CPU-profile flame graph, a trace's siblings
// can overlap in time (concurrent calls), so each span is placed on the first level at or
// below its parent's level + 1 whose already-placed bars don't overlap it - concurrent
// siblings fan out onto extra levels instead of being drawn on top of each other, and a
// child always sits below its parent. Pure and DOM-free, same as critical-path.ts.

import type { SpanDto } from '$lib/traces-api';

export interface FlameBar {
	span: SpanDto;
	level: number;
	/** ms since the trace's earliest span start. */
	startMs: number;
	endMs: number;
}

export interface FlameLayout {
	bars: FlameBar[];
	levels: number;
	traceStartMs: number;
	/** Floored at 1ms, same divide-by-zero guard as TraceWaterfall's totalMs. */
	totalMs: number;
}

function toMs(iso: string): number {
	return new Date(iso).getTime();
}

export function computeFlameLayout(spans: SpanDto[]): FlameLayout {
	if (spans.length === 0) return { bars: [], levels: 0, traceStartMs: 0, totalMs: 1 };

	const traceStartMs = Math.min(...spans.map((s) => toMs(s.startTime)));
	const traceEndMs = Math.max(...spans.map((s) => toMs(s.endTime)));

	// Same parent-resolution rules as TraceWaterfall's tree: a span whose parent isn't in
	// this trace is a root, siblings are walked in start order, `visited` guards cycles.
	const spanIds = new Set(spans.map((s) => s.spanId));
	const byParent = new Map<string, SpanDto[]>();
	for (const span of spans) {
		const parentKey = span.parentSpanId && spanIds.has(span.parentSpanId) ? span.parentSpanId : '';
		const siblings = byParent.get(parentKey);
		if (siblings) siblings.push(span);
		else byParent.set(parentKey, [span]);
	}
	for (const siblings of byParent.values()) {
		siblings.sort((a, b) => a.startTime.localeCompare(b.startTime));
	}

	// Per level, the [start, end] intervals already occupied. Touching intervals (one ends
	// exactly where the next starts) share a level.
	const occupied: [number, number][][] = [];
	function fits(level: number, start: number, end: number): boolean {
		for (const [s, e] of occupied[level] ?? []) {
			if (start < e && s < end) return false;
			// Two zero-width bars at the same instant would render on top of each other.
			if (start === end && s === e && s === start) return false;
		}
		return true;
	}

	const bars: FlameBar[] = [];
	const visited = new Set<string>();
	function place(span: SpanDto, minLevel: number) {
		if (visited.has(span.spanId)) return;
		visited.add(span.spanId);
		const startMs = toMs(span.startTime) - traceStartMs;
		const endMs = Math.max(toMs(span.endTime) - traceStartMs, startMs);
		let level = minLevel;
		while (!fits(level, startMs, endMs)) level++;
		(occupied[level] ??= []).push([startMs, endMs]);
		bars.push({ span, level, startMs, endMs });
		for (const child of byParent.get(span.spanId) ?? []) place(child, level + 1);
	}
	for (const root of byParent.get('') ?? []) place(root, 0);

	return { bars, levels: occupied.length, traceStartMs, totalMs: Math.max(1, traceEndMs - traceStartMs) };
}
