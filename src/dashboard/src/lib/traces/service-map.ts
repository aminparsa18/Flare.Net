// Builds a per-trace service dependency graph from its spans: which services this
// trace actually passed through, and who called whom - "the trace as a journey through
// your architecture," not just a list of spans. Framework-agnostic (plain data in, plain
// data out, unit-testable on its own) - same separation ResourceGraph.svelte keeps
// between its `$effect`'s SvelteFlow-specific node/edge shaping and the data it shapes.

import type { SpanDto } from '$lib/traces-api';

export interface ServiceMapNode {
	service: string;
	/** Spans attributed to this service within the trace. */
	spanCount: number;
	errorCount: number;
	/** Sum of durationNano across every span attributed to this service - not wall-clock time spent (spans can overlap, see critical-path.ts for that decomposition), just "how much work this service did." */
	totalDurationNano: number;
	/** Distinct span names attributed to this service, first-seen order - the operations it performed in this trace. */
	operations: string[];
}

export interface ServiceMapEdge {
	source: string;
	target: string;
	/** Spans crossing from source into target - one call graph edge can represent more than one call (e.g. a loop, or a burst-generated repeat). */
	callCount: number;
	/** Sum of durationNano across every crossing span - the calling span's own duration is the natural stand-in for "how long this call took." */
	totalDurationNano: number;
}

export interface ServiceMapResult {
	nodes: ServiceMapNode[];
	edges: ServiceMapEdge[];
}

/**
 * A span's service is normally its `serviceName` (the OTel Resource attribute - real,
 * one value per process). The standard `peer.service` span attribute overrides it when
 * present: the spec-correct way for a span to say "this call went out to service X,"
 * used by CLIENT spans representing a call into a system that isn't itself sending
 * Flare its own separately-instrumented spans (a real downstream dependency with no
 * OTel SDK of its own, or - see ExampleApp.LogGenerator's EmitWaterfall remarks - a
 * single-process demo simulating what a multi-service trace looks like).
 */
function effectiveService(span: SpanDto): string {
	return span.spanAttributes['peer.service'] || span.serviceName || 'unknown';
}

export function buildServiceMap(spans: SpanDto[]): ServiceMapResult {
	if (spans.length === 0) return { nodes: [], edges: [] };

	const byId = new Map(spans.map((s) => [s.spanId, s]));
	const nodes = new Map<string, ServiceMapNode>();
	const edges = new Map<string, ServiceMapEdge>();

	function touchNode(service: string, span: SpanDto): void {
		const isError = span.statusCode === 'STATUS_CODE_ERROR';
		const existing = nodes.get(service);
		if (existing) {
			existing.spanCount += 1;
			existing.totalDurationNano += span.durationNano;
			if (isError) existing.errorCount += 1;
			if (span.name && !existing.operations.includes(span.name)) existing.operations.push(span.name);
		} else {
			nodes.set(service, {
				service,
				spanCount: 1,
				errorCount: isError ? 1 : 0,
				totalDurationNano: span.durationNano,
				operations: span.name ? [span.name] : []
			});
		}
	}

	for (const span of spans) {
		const service = effectiveService(span);
		touchNode(service, span);

		const parent = span.parentSpanId ? byId.get(span.parentSpanId) : undefined;
		if (!parent) continue;

		const parentService = effectiveService(parent);
		if (parentService === service) continue; // Same-service call - not a dependency edge.

		const key = `${parentService}->${service}`;
		const existing = edges.get(key);
		if (existing) {
			existing.callCount += 1;
			existing.totalDurationNano += span.durationNano;
		} else {
			edges.set(key, { source: parentService, target: service, callCount: 1, totalDurationNano: span.durationNano });
		}
	}

	return { nodes: [...nodes.values()], edges: [...edges.values()] };
}
