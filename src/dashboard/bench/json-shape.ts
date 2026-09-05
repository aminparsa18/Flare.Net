// The JSON wire shape `Flare.Api.Json.LogsJsonContext` actually produces (camelCase,
// ISO-8601 date strings, plain objects for attribute maps - see that context's own
// header comment) and the hand-written parse back into the same field types the
// MemoryPack-generated decoder produces (`Date`/`bigint`), for an apples-to-apples
// comparison in memorypack-vs-json.bench.ts. This is the "plain JSON.parse/hand-written
// interface parsing" side of docs-internal/planning/roadmap.md's "Flare-specific
// JSON-vs-MemoryPack benchmark" item - pre-migration dashboard code used exactly this
// shape (see `src/lib/api.ts`'s own header comment and its `toISOString()`/`new Date()`
// conversions).

import type { LogEventDto } from '$lib/memorypack/LogEventDto';
import type { LogSearchResponse } from '$lib/memorypack/LogSearchResponse';

export interface LogEventDtoJson {
	eventId: string;
	timestamp: string;
	observedTimestamp: string;
	ingestedAt: string;
	traceId: string;
	spanId: string;
	traceFlags: number;
	severityText: string;
	severityNumber: number;
	serviceName: string;
	body: string;
	resourceSchemaUrl: string;
	resourceAttributes: Record<string, string>;
	scopeSchemaUrl: string;
	scopeName: string;
	scopeVersion: string;
	scopeAttributes: Record<string, string>;
	logAttributes: Record<string, string>;
	eventName: string;
	patternId: string;
	patternTemplate: string;
	spanDurationNano: number | null;
}

export interface LogSearchResponseJson {
	events: LogEventDtoJson[];
	nextCursor: string | null;
}

function mapToRecord(map: Map<string | null, string | null> | null): Record<string, string> {
	const out: Record<string, string> = {};
	if (map == null) return out;
	for (const [k, v] of map) {
		out[k ?? ''] = v ?? '';
	}
	return out;
}

/** `LogEventDto` (the in-memory class) -> the plain object `JSON.stringify` would send over the wire. */
export function toJsonShape(dto: LogEventDto): LogEventDtoJson {
	return {
		eventId: dto.eventId,
		timestamp: dto.timestamp.toISOString(),
		observedTimestamp: dto.observedTimestamp.toISOString(),
		ingestedAt: dto.ingestedAt.toISOString(),
		traceId: dto.traceId ?? '',
		spanId: dto.spanId ?? '',
		traceFlags: dto.traceFlags,
		severityText: dto.severityText ?? '',
		severityNumber: dto.severityNumber,
		serviceName: dto.serviceName ?? '',
		body: dto.body ?? '',
		resourceSchemaUrl: dto.resourceSchemaUrl ?? '',
		resourceAttributes: mapToRecord(dto.resourceAttributes),
		scopeSchemaUrl: dto.scopeSchemaUrl ?? '',
		scopeName: dto.scopeName ?? '',
		scopeVersion: dto.scopeVersion ?? '',
		scopeAttributes: mapToRecord(dto.scopeAttributes),
		logAttributes: mapToRecord(dto.logAttributes),
		eventName: dto.eventName ?? '',
		patternId: dto.patternId ?? '',
		patternTemplate: dto.patternTemplate ?? '',
		spanDurationNano: dto.spanDurationNano == null ? null : Number(dto.spanDurationNano)
	};
}

export function responseToJsonShape(response: LogSearchResponse): LogSearchResponseJson {
	return {
		events: (response.events ?? []).map((e) => toJsonShape(e!)),
		nextCursor: response.nextCursor
	};
}

/** Hand-written parse: `JSON.parse` output -> the same field types MemoryPack's decoder returns (`Date`/`bigint`). */
export function parseLogEventDtoJson(json: LogEventDtoJson): {
	eventId: string;
	timestamp: Date;
	observedTimestamp: Date;
	ingestedAt: Date;
	traceId: string;
	spanId: string;
	traceFlags: number;
	severityText: string;
	severityNumber: number;
	serviceName: string;
	body: string;
	resourceSchemaUrl: string;
	resourceAttributes: Record<string, string>;
	scopeSchemaUrl: string;
	scopeName: string;
	scopeVersion: string;
	scopeAttributes: Record<string, string>;
	logAttributes: Record<string, string>;
	eventName: string;
	patternId: string;
	patternTemplate: string;
	spanDurationNano: bigint | null;
} {
	return {
		...json,
		timestamp: new Date(json.timestamp),
		observedTimestamp: new Date(json.observedTimestamp),
		ingestedAt: new Date(json.ingestedAt),
		spanDurationNano: json.spanDurationNano == null ? null : BigInt(json.spanDurationNano)
	};
}

export function parseLogSearchResponseJson(json: LogSearchResponseJson) {
	return {
		events: json.events.map(parseLogEventDtoJson),
		nextCursor: json.nextCursor
	};
}
