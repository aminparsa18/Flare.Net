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
	type SpanAttributeBagName,
	type SpanAttributeFilterOperatorName
} from '$lib/memorypack/enums';
import { SpanFilter as GeneratedSpanFilter } from '$lib/memorypack/SpanFilter';
import { SpanAttributeFilter as GeneratedSpanAttributeFilter } from '$lib/memorypack/SpanAttributeFilter';
import { SpanSearchRequest as GeneratedSpanSearchRequest } from '$lib/memorypack/SpanSearchRequest';
import { SpanSearchResponse as GeneratedSpanSearchResponse } from '$lib/memorypack/SpanSearchResponse';
import { TraceDto as GeneratedTraceDto } from '$lib/memorypack/TraceDto';
import type { SpanDto as GeneratedSpanDto } from '$lib/memorypack/SpanDto';
import type { SpanEventDto as GeneratedSpanEventDto } from '$lib/memorypack/SpanEventDto';
import { SpanAttributeValuesRequest as GeneratedSpanAttributeValuesRequest } from '$lib/memorypack/SpanAttributeValuesRequest';
import { SpanAttributeValuesResponse as GeneratedSpanAttributeValuesResponse } from '$lib/memorypack/SpanAttributeValuesResponse';

// ---- Shared filter shape (SpanFilter.cs) -----------------------------------

export type SpanAttributeBag = SpanAttributeBagName;

/** See `SpanAttributeFilterOperator` (SpanFilter.cs). `Exists`/`Absent` ignore `SpanAttributeFilter.value`. */
export type SpanAttributeFilterOperator = SpanAttributeFilterOperatorName;

export interface SpanAttributeFilter {
	bag: SpanAttributeBag;
	key: string;
	value: string;
	/** Defaults to `'Equals'` when omitted - matches the backend's own default, and keeps every existing caller that only ever set bag/key/value unchanged. */
	operator?: SpanAttributeFilterOperator;
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
}

function toGeneratedSpanFilter(filter: SpanFilter | undefined): GeneratedSpanFilter {
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
	dto.attributes =
		filter.attributes == null
			? null
			: filter.attributes.map((a) => {
					const attr = new GeneratedSpanAttributeFilter();
					attr.bag = spanAttributeBagFromString(a.bag);
					attr.key = a.key;
					attr.value = a.value;
					attr.operator = spanAttributeFilterOperatorFromString(a.operator ?? 'Equals');
					return attr;
				});
	return dto;
}

// ---- Span DTO (SpanDto.cs) --------------------------------------------------

export interface SpanEventDto {
	timestamp: string;
	name: string;
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
	/** Total spans sharing this row's traceId - only populated for `SpanFilter.rootSpansOnly` searches (Flare's trace list view). See SpanDto.SpanCount's C# remarks. */
	spanCount?: number;
	/** Whether any span sharing this row's traceId - not just this row's own `statusCode` - carries "STATUS_CODE_ERROR". Same populated-when as `spanCount`. See SpanDto.HasError's C# remarks. */
	hasError?: boolean;
}

function toSpanEventDto(dto: GeneratedSpanEventDto): SpanEventDto {
	return {
		timestamp: dto.timestamp.toISOString(),
		name: dto.name ?? '',
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
		hasError: dto.hasError ?? undefined
	};
}

// ---- POST /api/spans/search (SpanSearchRequest.cs / SpanSearchResponse) ---

export interface SpanSearchRequest {
	filter?: SpanFilter;
	cursor?: string;
	pageSize?: number;
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
	const res = await apiFetch(`${API_BASE_URL}/api/spans/search`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSpanSearchRequest.serialize(dto)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/spans/search failed: ${res.status} ${res.statusText}`);
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
	return {
		traceId: dto.traceId ?? '',
		spans: (dto.spans ?? []).map((s) => toSpanDto(s!))
	};
}

// ---- POST /api/spans/attribute-values (SpanAttributeValuesRequest.cs) -----------------
// Value autocomplete for SpanAttributeFiltersRow.svelte's value input - the Traces page's
// equivalent of `$lib/api.ts`'s `getLogAttributeValues`. Every distinct value observed for
// one caller-chosen bag+key, most-observed first, optionally narrowed by `prefix`.

export interface SpanAttributeValuesRequest {
	filter?: SpanFilter;
	bag?: SpanAttributeBag;
	key: string;
	/** Case-insensitive substring already typed, if any - narrows candidates server-side. */
	prefix?: string;
	limit?: number;
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
