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
	hasError: boolean;
}

export interface ServiceMapEdge {
	source: string;
	target: string;
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
	const edgeKeys = new Set<string>();
	const edges: ServiceMapEdge[] = [];

	function touch(service: string, hasError: boolean): void {
		const existing = nodes.get(service);
		if (existing) {
			existing.spanCount += 1;
			existing.hasError ||= hasError;
		} else {
			nodes.set(service, { service, spanCount: 1, hasError });
		}
	}

	for (const span of spans) {
		const service = effectiveService(span);
		touch(service, span.statusCode === 'STATUS_CODE_ERROR');

		const parent = span.parentSpanId ? byId.get(span.parentSpanId) : undefined;
		if (!parent) continue;

		const parentService = effectiveService(parent);
		if (parentService === service) continue; // Same-service call - not a dependency edge.

		const key = `${parentService}->${service}`;
		if (!edgeKeys.has(key)) {
			edgeKeys.add(key);
			edges.push({ source: parentService, target: service });
		}
	}

	return { nodes: [...nodes.values()], edges };
}
