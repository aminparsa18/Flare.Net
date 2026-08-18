// Critical-path analysis for a trace's span tree: which spans (and how much of each)
// actually determined when the trace finished, as opposed to work that ran
// concurrently and simply never became the bottleneck. Standard trace-CPA
// decomposition (the same idea as Uber's "Critical Path Analysis for Microservice
// Architectures"): walk each span's children latest-end-first. Whichever child's
// interval abuts the current cursor extends the critical path into that child; any
// gap between the cursor and the next-latest child's end is the parent's own "self
// time" - work nothing else could hide behind. Recursing this way partitions the
// root's entire duration into segments, each attributed to exactly one span, so the
// segment durations always sum to exactly the root span's own duration - a span with
// no attributed segment ran entirely in the shadow of a slower sibling and had zero
// effect on the trace's end time.

import type { SpanDto } from '$lib/traces-api';

export interface CriticalPathResult {
	/** spanId -> total ms that span (specifically, not its descendants) contributed to the critical path. Absent = that span never determined the trace's end time. */
	contributionMs: Map<string, number>;
	/** The span whose own segment(s) contributed the most - usually the most useful single "what's slow" answer. */
	topContributor: { span: SpanDto; ms: number } | null;
	/** The root span the path was computed from (latest-ending among orphaned "roots", same tie-break TraceWaterfall's row builder uses). */
	root: SpanDto;
}

function startMs(span: SpanDto): number {
	return new Date(span.startTime).getTime();
}

function endMs(span: SpanDto): number {
	return new Date(span.endTime).getTime();
}

export function computeCriticalPath(spans: SpanDto[]): CriticalPathResult | null {
	if (spans.length === 0) return null;

	const spanIds = new Set(spans.map((s) => s.spanId));
	const byParent = new Map<string, SpanDto[]>();
	for (const span of spans) {
		const parentKey = span.parentSpanId && spanIds.has(span.parentSpanId) ? span.parentSpanId : '';
		const siblings = byParent.get(parentKey);
		if (siblings) siblings.push(span);
		else byParent.set(parentKey, [span]);
	}

	const roots = byParent.get('') ?? [];
	if (roots.length === 0) return null;
	const root = roots.reduce((latest, r) => (endMs(r) > endMs(latest) ? r : latest));

	const contributionMs = new Map<string, number>();
	function attribute(span: SpanDto, from: number, to: number) {
		if (to <= from) return;
		contributionMs.set(span.spanId, (contributionMs.get(span.spanId) ?? 0) + (to - from));
	}

	// `hardEnd` bounds how far this span's own interval is allowed to count for - a
	// child clipped by its parent's cursor (because an even-later-ending sibling
	// already claimed that tail) only gets credit up to where its parent's claim
	// actually starts.
	function walk(span: SpanDto, hardEnd: number) {
		const children = [...(byParent.get(span.spanId) ?? [])].sort((a, b) => endMs(b) - endMs(a));
		let cursor = Math.min(hardEnd, endMs(span));
		for (const child of children) {
			const childEnd = Math.min(endMs(child), cursor);
			if (childEnd <= startMs(child)) continue; // no overlap left to claim
			attribute(span, childEnd, cursor);
			walk(child, childEnd);
			cursor = Math.min(cursor, startMs(child));
		}
		attribute(span, startMs(span), cursor);
	}

	walk(root, endMs(root));

	let topContributor: CriticalPathResult['topContributor'] = null;
	for (const span of spans) {
		const ms = contributionMs.get(span.spanId);
		if (ms && (!topContributor || ms > topContributor.ms)) {
			topContributor = { span, ms };
		}
	}

	return { contributionMs, topContributor, root };
}
