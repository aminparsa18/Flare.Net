// The client half of a dashboard request, per format: response bytes -> the plain types the UI
// consumes (`LogSearchResponse`, `TraceDto`, `MetricQueryResponse` from `src/lib/*-api.ts`).
//
// MemoryPack: the generated decoder plus the production mapper (`toLogEventDto` etc., imported
// from the real modules). This is exactly what `searchLogs`/`getTrace`/`queryMetric` do with
// `await res.arrayBuffer()`.
//
// JSON: what a JSON consumer would do with `await res.json()` - UTF-8 decode, `JSON.parse`, then a
// field-by-field copy into the same UI types with the same `?? ''` defaults. Production has no JSON
// consumer for these endpoints any more, so this mapper is the benchmark's own; it is held to the
// MemoryPack path's output by `assertEquivalent` before anything is timed.

import { brotliDecompressSync, gunzipSync } from 'node:zlib';
import { isDeepStrictEqual } from 'node:util';
import { LogSearchResponse as GeneratedLogSearchResponse } from '#lib/generated/memorypack/LogSearchResponse.ts';
import { TraceDto as GeneratedTraceDto } from '#lib/generated/memorypack/TraceDto.ts';
import { MetricQueryResponse as GeneratedMetricQueryResponse } from '#lib/generated/memorypack/MetricQueryResponse.ts';
import { toLogEventDto, type LogEventDto, type LogSearchResponse } from '#lib/api.ts';
import { toTraceDto, type SpanDto, type TraceDto } from '#lib/traces-api.ts';
import { toMetricSeries, type MetricQueryResponse, type MetricSeries, type MetricSeriesPoint } from '#lib/metrics-api.ts';
import type { Format, Kind } from './fixtures.ts';

// ---- decompression (the browser does this natively in its network stack; Node's zlib is native too)

export function decompress(codec: string, bytes: Uint8Array<ArrayBuffer>): Uint8Array<ArrayBuffer> {
	switch (codec) {
		case 'none':
			return bytes;
		case 'gzip-1':
		case 'gzip-6':
			return ownBuffer(gunzipSync(bytes));
		case 'brotli-4':
			return ownBuffer(brotliDecompressSync(bytes));
		default:
			throw new Error(`Unknown codec '${codec}'`);
	}
}

/** A zlib result may be a view into a larger pooled buffer; the MemoryPack decoder needs an ArrayBuffer of exactly the body. */
function ownBuffer(buf: Buffer): Uint8Array<ArrayBuffer> {
	if (buf.byteOffset === 0 && buf.buffer.byteLength === buf.byteLength) {
		return new Uint8Array(buf.buffer as ArrayBuffer);
	}
	return new Uint8Array(buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength) as ArrayBuffer);
}

// ---- decode only (no mapping) ----------------------------------------------------------------

const utf8 = new TextDecoder();

export function decodeOnly(kind: Kind, format: Format, bytes: Uint8Array<ArrayBuffer>): unknown {
	if (format === 'json') {
		return JSON.parse(utf8.decode(bytes));
	}
	switch (kind) {
		case 'logs':
			return GeneratedLogSearchResponse.deserialize(bytes.buffer);
		case 'trace':
			return GeneratedTraceDto.deserialize(bytes.buffer);
		case 'metrics':
			return GeneratedMetricQueryResponse.deserialize(bytes.buffer);
	}
}

// ---- decode + map into the UI types -----------------------------------------------------------

export function decodeAndMap(kind: Kind, format: Format, bytes: Uint8Array<ArrayBuffer>): LogSearchResponse | TraceDto | MetricQueryResponse {
	if (format === 'json') {
		const json = JSON.parse(utf8.decode(bytes));
		switch (kind) {
			case 'logs':
				return logsFromJson(json);
			case 'trace':
				return traceFromJson(json);
			case 'metrics':
				return metricsFromJson(json);
		}
	}
	switch (kind) {
		case 'logs': {
			const body = GeneratedLogSearchResponse.deserialize(bytes.buffer);
			return { events: (body?.events ?? []).map((e) => toLogEventDto(e!)), nextCursor: body?.nextCursor ?? null };
		}
		case 'trace':
			return toTraceDto(GeneratedTraceDto.deserialize(bytes.buffer)!);
		case 'metrics': {
			const body = GeneratedMetricQueryResponse.deserialize(bytes.buffer);
			return { series: (body?.series ?? []).map((s) => toMetricSeries(s!)), treatedAsCounter: body?.treatedAsCounter ?? false };
		}
	}
}

// ---- JSON mappers (System.Text.Json camelCase wire shape -> UI types) ---------------------------

/* eslint-disable @typescript-eslint/no-explicit-any -- untyped JSON.parse output, the same as `res.json()`. */
type Json = any;

function logsFromJson(j: Json): LogSearchResponse {
	return { events: (j.events ?? []).map(logEventFromJson), nextCursor: j.nextCursor ?? null };
}

function logEventFromJson(j: Json): LogEventDto {
	return {
		eventId: j.eventId,
		timestamp: j.timestamp,
		observedTimestamp: j.observedTimestamp,
		traceId: j.traceId ?? '',
		spanId: j.spanId ?? '',
		traceFlags: j.traceFlags,
		severityText: j.severityText ?? '',
		severityNumber: j.severityNumber,
		serviceName: j.serviceName ?? '',
		body: j.body ?? '',
		resourceSchemaUrl: j.resourceSchemaUrl ?? '',
		resourceAttributes: j.resourceAttributes ?? {},
		scopeSchemaUrl: j.scopeSchemaUrl ?? '',
		scopeName: j.scopeName ?? '',
		scopeVersion: j.scopeVersion ?? '',
		scopeAttributes: j.scopeAttributes ?? {},
		logAttributes: j.logAttributes ?? {},
		eventName: j.eventName ?? '',
		patternId: j.patternId ?? '',
		patternTemplate: j.patternTemplate ?? '',
		spanDurationNano: j.spanDurationNano ?? null,
		droppedAttributesCount: j.droppedAttributesCount
	};
}

function traceFromJson(j: Json): TraceDto {
	return {
		traceId: j.traceId ?? '',
		spans: (j.spans ?? []).map(spanFromJson),
		truncated: j.truncated,
		partialSpanIds: (j.partialSpanIds ?? []).map((id: string | null) => id ?? '')
	};
}

function spanFromJson(j: Json): SpanDto {
	return {
		traceId: j.traceId ?? '',
		spanId: j.spanId ?? '',
		parentSpanId: j.parentSpanId ?? '',
		traceState: j.traceState ?? '',
		name: j.name ?? '',
		kind: j.kind,
		startTime: j.startTime,
		endTime: j.endTime,
		durationNano: j.durationNano,
		statusCode: j.statusCode ?? '',
		statusMessage: j.statusMessage ?? '',
		serviceName: j.serviceName ?? '',
		resourceSchemaUrl: j.resourceSchemaUrl ?? '',
		resourceAttributes: j.resourceAttributes ?? {},
		scopeSchemaUrl: j.scopeSchemaUrl ?? '',
		scopeName: j.scopeName ?? '',
		scopeVersion: j.scopeVersion ?? '',
		scopeAttributes: j.scopeAttributes ?? {},
		spanAttributes: j.spanAttributes ?? {},
		events: (j.events ?? []).map((e: Json) => ({ timestamp: e.timestamp, name: e.name ?? '', attributes: e.attributes ?? {} })),
		spanCount: j.spanCount ?? undefined,
		hasError: j.hasError ?? undefined,
		links: (j.links ?? []).map((l: Json) => ({
			traceId: l.traceId ?? '',
			spanId: l.spanId ?? '',
			traceState: l.traceState ?? '',
			attributes: l.attributes ?? {}
		})),
		droppedAttributesCount: j.droppedAttributesCount,
		droppedEventsCount: j.droppedEventsCount,
		droppedLinksCount: j.droppedLinksCount
	};
}

function metricsFromJson(j: Json): MetricQueryResponse {
	return { series: (j.series ?? []).map(seriesFromJson), treatedAsCounter: j.treatedAsCounter ?? false };
}

function seriesFromJson(j: Json): MetricSeries {
	return { serviceName: j.serviceName ?? '', attributes: j.attributes ?? {}, points: (j.points ?? []).map(pointFromJson) };
}

function pointFromJson(j: Json): MetricSeriesPoint {
	return {
		bucketStart: j.bucketStart,
		value: j.value ?? null,
		count: j.count ?? null,
		sum: j.sum ?? null,
		p50: j.p50 ?? null,
		p75: j.p75 ?? null,
		p90: j.p90 ?? null,
		p95: j.p95 ?? null,
		p99: j.p99 ?? null,
		maxApprox: j.maxApprox ?? null,
		bucketLowers: j.bucketLowers ?? null,
		bucketUppers: j.bucketUppers ?? null,
		bucketCounts: j.bucketCounts ?? null
	};
}
/* eslint-enable @typescript-eslint/no-explicit-any */

// ---- equivalence ---------------------------------------------------------------------------

const ISO_TIMESTAMP = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/;

/**
 * Both formats must hand the UI the same data, or the timings compare different work. Timestamps
 * are compared as instants at millisecond precision: STJ sends `...00.1234567+00:00`, the
 * MemoryPack path `Date.toISOString()` (`...00.123Z`), and both are valid UI values.
 */
export function assertEquivalent(scenario: string, kind: Kind, json: Uint8Array<ArrayBuffer>, memoryPack: Uint8Array<ArrayBuffer>): void {
	const a = normalize(decodeAndMap(kind, 'json', json));
	const b = normalize(decodeAndMap(kind, 'memorypack', memoryPack));
	if (!isDeepStrictEqual(a, b)) {
		throw new Error(`${scenario}: the JSON and MemoryPack paths produce different UI data - fix bench/support/client.ts before trusting any timing.`);
	}
}

function normalize(value: unknown): unknown {
	return JSON.parse(JSON.stringify(value), (_key, v) => (typeof v === 'string' && ISO_TIMESTAMP.test(v) ? new Date(v).toISOString() : v));
}
