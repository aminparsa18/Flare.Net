// Client for Flare.Api's traces Query API (span search, get-trace-by-id). Field
// names/casing are a hand-mirror of src/Flare.Api/Model/SpanFilter.cs,
// Model/SpanDto.cs, Model/SpanSearchRequest.cs + Json/SpansJsonContext.cs - same
// camelCase-properties/PascalCase-string-enum-values convention `api.ts` documents for
// LogsJsonContext. Keep in sync with those files by hand.

import { API_BASE_URL } from './api';

// ---- Shared filter shape (SpanFilter.cs) -----------------------------------

export type SpanAttributeBag = 'Span' | 'Resource' | 'Scope';

export interface SpanAttributeFilter {
	bag: SpanAttributeBag;
	key: string;
	value: string;
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
	const res = await fetch(`${API_BASE_URL}/api/spans/search`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/spans/search failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

// ---- GET /api/traces/{traceId} (TraceDto) ----------------------------------

export interface TraceDto {
	traceId: string;
	/** Ascending by startTime - the order a waterfall renders top-to-bottom. */
	spans: SpanDto[];
}

/** Returns `null` for a 404 (no spans found for that trace id) rather than throwing - a normal, expected outcome the caller renders as "not found," not an error state. */
export async function getTrace(traceId: string, signal?: AbortSignal): Promise<TraceDto | null> {
	const res = await fetch(`${API_BASE_URL}/api/traces/${encodeURIComponent(traceId)}`, { signal });
	if (res.status === 404) {
		return null;
	}
	if (!res.ok) {
		throw new Error(`GET /api/traces/${traceId} failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
