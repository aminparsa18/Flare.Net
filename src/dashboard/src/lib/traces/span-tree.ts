// Parent-before-children ordering of a trace's spans. Shared by TraceWaterfall (its row
// order) and TraceDetailState's span search (the order prev/next steps through matches),
// so stepping always moves down the waterfall rather than jumping around by start time.

import type { SpanDto } from '$lib/traces-api';

export interface SpanTreeRow {
	span: SpanDto;
	depth: number;
	/** Total spans in this row's subtree, excluding itself. */
	descendants: number;
}

/**
 * Flattens the trace's spans into parent-before-children render order with a depth
 * per row, via a straightforward tree walk. A span whose `parentSpanId` doesn't
 * point at another span in this trace (absent, or - defensively - a parent that
 * hasn't landed/was dropped) is treated as a root rather than silently omitted, so
 * every fetched span always renders somewhere. `visited` guards against a
 * malformed/cyclic parent reference looping forever - real OTLP data never does
 * this, but nothing upstream validates it either.
 */
export function buildSpanTree(spans: SpanDto[]): SpanTreeRow[] {
	if (spans.length === 0) return [];

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

	const result: SpanTreeRow[] = [];
	const visited = new Set<string>();
	function visit(span: SpanDto, depth: number) {
		if (visited.has(span.spanId)) return;
		visited.add(span.spanId);
		const row: SpanTreeRow = { span, depth, descendants: 0 };
		const index = result.push(row);
		for (const child of byParent.get(span.spanId) ?? []) visit(child, depth + 1);
		row.descendants = result.length - index;
	}
	for (const root of byParent.get('') ?? []) visit(root, 0);
	return result;
}

/**
 * Case-insensitive substring match of the trace-detail search box against one span:
 * its name, service, span id, status message, and every span/resource attribute key
 * and value. Each attribute is matched as one "key=value" string - which covers the
 * key and the value on their own, and also lets `http.response.status_code=500` find
 * exactly that pair. Purely client-side over the already-loaded trace - no API call.
 */
export function spanMatchesSearch(span: SpanDto, query: string): boolean {
	const q = query.trim().toLowerCase();
	if (!q) return false;
	const has = (s: string) => s.toLowerCase().includes(q);
	if (has(span.name) || has(span.serviceName) || has(span.spanId) || has(span.statusMessage)) return true;
	for (const attributes of [span.spanAttributes, span.resourceAttributes]) {
		for (const [key, value] of Object.entries(attributes)) {
			if (has(`${key}=${value}`)) return true;
		}
	}
	return false;
}
