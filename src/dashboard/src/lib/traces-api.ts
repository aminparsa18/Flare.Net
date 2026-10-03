// Client for Flare.Api's traces Query API (span search, get-trace-by-id).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape. Every type in
// this file nests a `DateTimeOffset` member somewhere (`SpanDto.StartTime`/`EndTime`/
// `IngestedAt`, `SpanEventDto.Timestamp`, `SpanFilter.From`/`To`), so all are hand-written
// (`$lib/memorypack/`). `bag` converts through `$lib/memorypack/enums.ts`'s
// `spanAttributeBagToString`/`FromString`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import {
	spanAttributeBagFromString,
	spanAttributeFilterOperatorFromString,
	spanValuesFieldFromString,
	type SpanAttributeBagName,
	type SpanValuesFieldName,
	type SpanAttributeFilterOperatorName
} from '$lib/memorypack/enums';
import { SpanFilter as GeneratedSpanFilter } from '$lib/memorypack/SpanFilter';
import { SpanAttributeFilter as GeneratedSpanAttributeFilter } from '$lib/memorypack/SpanAttributeFilter';
import { TraceSpanCondition as GeneratedTraceSpanCondition } from '$lib/memorypack/TraceSpanCondition';
import { TraceStructureFilter as GeneratedTraceStructureFilter } from '$lib/memorypack/TraceStructureFilter';
import { SpanSearchRequest as GeneratedSpanSearchRequest } from '$lib/memorypack/SpanSearchRequest';
import { SpanSearchResponse as GeneratedSpanSearchResponse } from '$lib/memorypack/SpanSearchResponse';
import { TraceDto as GeneratedTraceDto } from '$lib/memorypack/TraceDto';
import type { SpanDto as GeneratedSpanDto } from '$lib/memorypack/SpanDto';
import type { SpanEventDto as GeneratedSpanEventDto } from '$lib/memorypack/SpanEventDto';
import type { SpanLinkDto as GeneratedSpanLinkDto } from '$lib/memorypack/SpanLinkDto';
import { SpanAttributeValuesRequest as GeneratedSpanAttributeValuesRequest } from '$lib/memorypack/SpanAttributeValuesRequest';
import { SpanAttributeValuesResponse as GeneratedSpanAttributeValuesResponse } from '$lib/memorypack/SpanAttributeValuesResponse';

// ---- Shared filter shape (SpanFilter.cs) -----------------------------------

export type SpanAttributeBag = SpanAttributeBagName;

/** See `SpanAttributeFilterOperator` (SpanFilter.cs). `Exists`/`Absent` ignore `SpanAttributeFilter.value`; `In`/`NotIn` ignore it too, taking their operand from `SpanAttributeFilter.values` instead. */
export type SpanAttributeFilterOperator = SpanAttributeFilterOperatorName;

export interface SpanAttributeFilter {
	bag: SpanAttributeBag;
	key: string;
	value: string;
	/** Defaults to `'Equals'` when omitted - matches the backend's own default, and keeps every existing caller that only ever set bag/key/value unchanged. */
	operator?: SpanAttributeFilterOperator;
	/** Operand for `'In'`/`'NotIn'` - ignored (may be omitted) for every other operator. See `SpanAttributeFilter.Values` (SpanFilter.cs). */
	values?: string[];
}

export interface SpanFilter {
	from?: string;
	to?: string;
	services?: string[];
	kinds?: number[];
	statusCodes?: string[];
	traceId?: string;
	rootSpansOnly?: boolean;
	minDurationNano?: number;
	maxDurationNano?: number;
	attributes?: SpanAttributeFilter[];
	/** Exact span-name (operation) match, any of. */
	names?: string[];
	/** Each service's entry spans only (no parent, or a parent in another service) - see `SpanFilter.EntrySpansOnly` (SpanFilter.cs). */
	entrySpansOnly?: boolean;
	/** Only spans of traces whose span tree matches - see `SpanFilter.Structure` (SpanFilter.cs) and `TraceStructureSqlBuilder`. */
	structure?: TraceStructureFilter;
	/** Only traces containing an N+1 pattern - see `SpanFilter.NPlusOneOnly` (SpanFilter.cs). */
	nPlusOneOnly?: boolean;
}

/** One lettered span condition of a structural trace query - see `TraceSpanCondition` (TraceStructureModels.cs). Unset/empty fields match anything. */
export interface TraceSpanCondition {
	/** `A`-`Z`, what the expression refers to it by. */
	name: string;
	serviceName?: string;
	spanName?: string;
	/** `STATUS_CODE_*` label. */
	statusCode?: string;
	minDurationNano?: number;
	attributes?: SpanAttributeFilter[];
}

/** Conditions plus an expression over them, e.g. `A -> B AND NOT C` - see `TraceStructureFilter` (TraceStructureModels.cs). */
export interface TraceStructureFilter {
	conditions: TraceSpanCondition[];
	expression: string;
}

/** The most conditions one structure takes - `TraceStructureSqlBuilder.MaxConditions`. */
export const MAX_STRUCTURE_CONDITIONS = 6;

function toGeneratedSpanAttributeFilter(a: SpanAttributeFilter): GeneratedSpanAttributeFilter {
	const attr = new GeneratedSpanAttributeFilter();
	attr.bag = spanAttributeBagFromString(a.bag);
	attr.key = a.key;
	attr.value = a.value;
	attr.operator = spanAttributeFilterOperatorFromString(a.operator ?? 'Equals');
	attr.values = a.values ?? null;
	return attr;
}

function toGeneratedStructure(structure: TraceStructureFilter): GeneratedTraceStructureFilter {
	const dto = new GeneratedTraceStructureFilter();
	dto.expression = structure.expression;
	dto.conditions = structure.conditions.map((c) => {
		const condition = new GeneratedTraceSpanCondition();
		condition.name = c.name;
		condition.serviceName = c.serviceName || null;
		condition.spanName = c.spanName || null;
		condition.statusCode = c.statusCode || null;
		condition.minDurationNano = c.minDurationNano == null ? null : BigInt(c.minDurationNano);
		condition.attributes = c.attributes?.length ? c.attributes.map(toGeneratedSpanAttributeFilter) : null;
		return condition;
	});
	return dto;
}

/**
 * Checks a structure server-side (`POST /api/traces/structure/validate`, parse + validate
 * only, no query) - resolves to `null` when it's valid, or the reason it isn't.
 */
export async function validateTraceStructure(structure: TraceStructureFilter, signal?: AbortSignal): Promise<string | null> {
	const res = await apiFetch(`${API_BASE_URL}/api/traces/structure/validate`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedTraceStructureFilter.serialize(toGeneratedStructure(structure))),
		signal
	});
	if (res.ok) return null;
	let message = `POST /api/traces/structure/validate failed: ${res.status} ${res.statusText}`;
	try {
		const problem = await res.json();
		message = problem?.detail || problem?.title || message;
	} catch {
		// Not JSON - keep the generic message.
	}
	return message;
}

export function toGeneratedSpanFilter(filter: SpanFilter | undefined): GeneratedSpanFilter {
	const dto = new GeneratedSpanFilter();
	if (filter == null) return dto;
	dto.from = filter.from == null ? null : new Date(filter.from);
	dto.to = filter.to == null ? null : new Date(filter.to);
	dto.services = filter.services ?? null;
	dto.kinds = filter.kinds ?? null;
	dto.statusCodes = filter.statusCodes ?? null;
	dto.traceId = filter.traceId ?? null;
	dto.rootSpansOnly = filter.rootSpansOnly ?? false;
	dto.minDurationNano = filter.minDurationNano == null ? null : BigInt(filter.minDurationNano);
	dto.maxDurationNano = filter.maxDurationNano == null ? null : BigInt(filter.maxDurationNano);
	dto.attributes = filter.attributes == null ? null : filter.attributes.map(toGeneratedSpanAttributeFilter);
	dto.names = filter.names ?? null;
	dto.entrySpansOnly = filter.entrySpansOnly ?? false;
	dto.structure = filter.structure == null ? null : toGeneratedStructure(filter.structure);
	dto.nPlusOneOnly = filter.nPlusOneOnly ?? false;
	return dto;
}

// ---- Span DTO (SpanDto.cs) --------------------------------------------------

export interface SpanEventDto {
	timestamp: string;
	name: string;
	attributes: Record<string, string>;
}

/** One entry of a span's `links` (OTLP Span.Links - references to another span, in the same or a different trace). Unlike SpanEventDto, carries no timestamp of its own. */
export interface SpanLinkDto {
	traceId: string;
	spanId: string;
	/** Empty string when absent - same convention as SpanDto.traceState. */
	traceState: string;
	attributes: Record<string, string>;
}

export interface SpanDto {
	traceId: string;
	spanId: string;
	/** Empty string = root span - same "empty string means absent" convention as LogEventDto's TraceId/SpanId. */
	parentSpanId: string;
	traceState: string;
	name: string;
	/** OTel SpanKind: 0=unspecified, 1=internal, 2=server, 3=client, 4=producer, 5=consumer. */
	kind: number;
	startTime: string;
	endTime: string;
	durationNano: number;
	/** The ClickHouse Enum8 label as-is, e.g. "STATUS_CODE_OK" - see SpanFilter.statusCodes' remarks in the C# source for why this isn't re-encoded. */
	statusCode: string;
	statusMessage: string;
	serviceName: string;
	resourceSchemaUrl: string;
	resourceAttributes: Record<string, string>;
	scopeSchemaUrl: string;
	scopeName: string;
	scopeVersion: string;
	scopeAttributes: Record<string, string>;
	spanAttributes: Record<string, string>;
	events: SpanEventDto[];
	/** Total spans sharing this row's traceId - only populated for `SpanFilter.rootSpansOnly`/`entrySpansOnly` searches (Flare's trace list view). See SpanDto.SpanCount's C# remarks. */
	spanCount?: number;
	/** Whether any span sharing this row's traceId - not just this row's own `statusCode` - carries "STATUS_CODE_ERROR". Same populated-when as `spanCount`. See SpanDto.HasError's C# remarks. */
	hasError?: boolean;
	/** OTLP Span.Links - references from this span to a span in the same or a different trace. See SpanDto.Links' C# remarks. */
	links: SpanLinkDto[];
}

function toSpanEventDto(dto: GeneratedSpanEventDto): SpanEventDto {
	return {
		timestamp: dto.timestamp.toISOString(),
		name: dto.name ?? '',
		attributes: dto.attributes ?? {}
	};
}

function toSpanLinkDto(dto: GeneratedSpanLinkDto): SpanLinkDto {
	return {
		traceId: dto.traceId ?? '',
		spanId: dto.spanId ?? '',
		traceState: dto.traceState ?? '',
		attributes: dto.attributes ?? {}
	};
}

function toSpanDto(dto: GeneratedSpanDto): SpanDto {
	return {
		traceId: dto.traceId ?? '',
		spanId: dto.spanId ?? '',
		parentSpanId: dto.parentSpanId ?? '',
		traceState: dto.traceState ?? '',
		name: dto.name ?? '',
		kind: dto.kind,
		startTime: dto.startTime.toISOString(),
		endTime: dto.endTime.toISOString(),
		durationNano: Number(dto.durationNano),
		statusCode: dto.statusCode ?? '',
		statusMessage: dto.statusMessage ?? '',
		serviceName: dto.serviceName ?? '',
		resourceSchemaUrl: dto.resourceSchemaUrl ?? '',
		resourceAttributes: dto.resourceAttributes ?? {},
		scopeSchemaUrl: dto.scopeSchemaUrl ?? '',
		scopeName: dto.scopeName ?? '',
		scopeVersion: dto.scopeVersion ?? '',
		scopeAttributes: dto.scopeAttributes ?? {},
		spanAttributes: dto.spanAttributes ?? {},
		events: (dto.events ?? []).map((e) => toSpanEventDto(e!)),
		spanCount: dto.spanCount == null ? undefined : Number(dto.spanCount),
		hasError: dto.hasError ?? undefined,
		links: (dto.links ?? []).map((l) => toSpanLinkDto(l!))
	};
}

// ---- POST /api/spans/search (SpanSearchRequest.cs / SpanSearchResponse) ---

/** Mirrors `SpanSortKey` (SpanSearchRequest.cs) - order matters, the wire carries the ordinal. */
export const SPAN_SORT_KEYS = ['StartTime', 'Duration', 'SpanCount'] as const;
export type SpanSortKey = (typeof SPAN_SORT_KEYS)[number];

export interface SpanSearchRequest {
	filter?: SpanFilter;
	cursor?: string;
	pageSize?: number;
	/** Defaults to `StartTime`. Each key breaks ties on (traceId, spanId). */
	sortBy?: SpanSortKey;
	/** Defaults to `false` - newest / slowest / most-spans first. */
	sortAscending?: boolean;
}

export interface SpanSearchResponse {
	spans: SpanDto[];
	nextCursor: string | null;
}

export async function searchSpans(request: SpanSearchRequest = {}, signal?: AbortSignal): Promise<SpanSearchResponse> {
	const dto = new GeneratedSpanSearchRequest();
	dto.filter = toGeneratedSpanFilter(request.filter);
	dto.cursor = request.cursor ?? null;
	dto.pageSize = request.pageSize ?? null;
	dto.sortBy = Math.max(0, SPAN_SORT_KEYS.indexOf(request.sortBy ?? 'StartTime'));
	dto.sortAscending = request.sortAscending ?? false;
	const res = await apiFetch(`${API_BASE_URL}/api/spans/search`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSpanSearchRequest.serialize(dto)),
		signal
	});
	if (!res.ok) {
		// A 400 carries a ProblemDetails `detail` worth showing verbatim (an invalid
		// `filter.structure`, e.g. "The expression uses condition D, which isn't defined") -
		// same handling as api.ts's runLogQlQuery.
		let message = `POST /api/spans/search failed: ${res.status} ${res.statusText}`;
		if (res.status === 400) {
			try {
				const problem = await res.json();
				message = problem?.detail || problem?.title || message;
			} catch {
				// Not JSON - keep the generic message.
			}
		}
		throw new Error(message);
	}
	const body = GeneratedSpanSearchResponse.deserialize(await res.arrayBuffer());
	return {
		spans: (body?.spans ?? []).map((s) => toSpanDto(s!)),
		nextCursor: body?.nextCursor ?? null
	};
}

// ---- GET /api/traces/{traceId} (TraceDto) ----------------------------------

export interface TraceDto {
	traceId: string;
	/** Ascending by startTime - the order a waterfall renders top-to-bottom. */
	spans: SpanDto[];
	/** True when the trace exceeded the server's span cap and `spans` holds only the earliest ones. */
	truncated: boolean;
	/** When `truncated`, loaded spans whose children are not loaded yet - fetch them with `getTraceChildren`. */
	partialSpanIds: string[];
}

function toTraceDto(dto: GeneratedTraceDto): TraceDto {
	return {
		traceId: dto.traceId ?? '',
		spans: (dto.spans ?? []).map((s) => toSpanDto(s!)),
		truncated: dto.truncated,
		partialSpanIds: (dto.partialSpanIds ?? []).map((id) => id ?? '')
	};
}

/** Returns `null` for a 404 (no spans found for that trace id) rather than throwing - a normal, expected outcome the caller renders as "not found," not an error state. */
export async function getTrace(traceId: string, signal?: AbortSignal): Promise<TraceDto | null> {
	const res = await apiFetch(`${API_BASE_URL}/api/traces/${encodeURIComponent(traceId)}`, { headers: memoryPackAcceptHeaders(), signal });
	if (res.status === 404) {
		return null;
	}
	if (!res.ok) {
		throw new Error(`GET /api/traces/${traceId} failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedTraceDto.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding TraceDto.');
	}
	return toTraceDto(dto);
}

/** The next depth levels below one span of a lazily loaded (`truncated`) trace. */
export async function getTraceChildren(traceId: string, spanId: string, signal?: AbortSignal): Promise<TraceDto> {
	const url = `${API_BASE_URL}/api/traces/${encodeURIComponent(traceId)}/spans/${encodeURIComponent(spanId)}/children`;
	const res = await apiFetch(url, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET ${url} failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedTraceDto.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding TraceDto.');
	}
	return toTraceDto(dto);
}

// ---- POST /api/spans/attribute-values (SpanAttributeValuesRequest.cs) -----------------
// Value autocomplete for SpanAttributeFiltersRow.svelte's value input - the Traces page's
// equivalent of `$lib/api.ts`'s `getLogAttributeValues`. Every distinct value observed for
// one caller-chosen bag+key, most-observed first, optionally narrowed by `prefix`.

export type SpanValuesField = SpanValuesFieldName;

export interface SpanAttributeValuesRequest {
	filter?: SpanFilter;
	bag?: SpanAttributeBag;
	/** Ignored (may be `''`) unless `field` is `'Attribute'`. */
	key: string;
	/** Case-insensitive substring already typed, if any - narrows candidates server-side. */
	prefix?: string;
	limit?: number;
	/** Defaults to `'Attribute'` (bag + key). The other fields enumerate built-in columns for the facet sidebar - see `SpanValuesField` in SpanAttributeValuesRequest.cs for each value's string form. */
	field?: SpanValuesField;
}

export interface SpanAttributeValueInfo {
	value: string;
	count: number;
}

export interface SpanAttributeValuesResponse {
	values: SpanAttributeValueInfo[];
}

export async function getSpanAttributeValues(
	request: SpanAttributeValuesRequest,
	signal?: AbortSignal
): Promise<SpanAttributeValuesResponse> {
	const dto = new GeneratedSpanAttributeValuesRequest();
	dto.filter = toGeneratedSpanFilter(request.filter);
	dto.bag = spanAttributeBagFromString(request.bag ?? 'Span');
	dto.key = request.key;
	dto.prefix = request.prefix ?? null;
	dto.limit = request.limit ?? 25;
	dto.field = spanValuesFieldFromString(request.field ?? 'Attribute');
	const res = await apiFetch(`${API_BASE_URL}/api/spans/attribute-values`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSpanAttributeValuesRequest.serialize(dto)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/spans/attribute-values failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedSpanAttributeValuesResponse.deserialize(await res.arrayBuffer());
	return {
		values: (body?.values ?? []).map((v) => ({ value: v!.value ?? '', count: Number(v!.count) }))
	};
}

// ---- POST /api/spans/duration-percentile (SpanDurationPercentileRequest.cs) -----------
// Where one span's duration ranks among spans with the same service + name within +/-1h of
// it - SpanDetailSheet's "p97 of GET /orders" line. JSON both ways (no MemoryPack twin).

export interface SpanDurationPercentile {
	sampleCount: number;
	/** Share of compared spans no longer than this one, 0-100. */
	percentile: number;
	p50Nano: number;
	p95Nano: number;
	p99Nano: number;
}

export async function getSpanDurationPercentile(
	span: { serviceName: string; name: string; durationNano: number; startTime: string },
	signal?: AbortSignal
): Promise<SpanDurationPercentile> {
	const res = await apiFetch(`${API_BASE_URL}/api/spans/duration-percentile`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(span),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/spans/duration-percentile failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
