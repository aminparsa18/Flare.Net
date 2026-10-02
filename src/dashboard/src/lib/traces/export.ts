// Client-side export for the Traces explorer list and the trace detail page. No backend
// endpoint, same approach as `$lib/logs/export.ts` (whose `downloadBlob` and row cap this reuses).
//  - List: the trace rows (one per trace, or per entry span) as CSV or NDJSON with a
//    user-chosen column subset. "visible" = the rows loaded in the table; "filtered" = every
//    row matching the filter, paginated via /api/spans/search up to EXPORT_ROW_CAP.
//  - Detail: the whole loaded trace as one JSON document (`traceToJson`).

import { searchSpans, type SpanDto, type SpanFilter, type SpanSortKey, type TraceDto } from '$lib/traces-api';
import { EXPORT_ROW_CAP } from '$lib/logs/export';
import type { ResolvedTimeRange } from '$lib/logs/time-range';

export { downloadBlob } from '$lib/logs/export';
export { EXPORT_ROW_CAP };

/** Backend's own SpanSearchQueryBuilder max PageSize. */
const EXPORT_PAGE_SIZE = 1000;

export type TraceExportFormat = 'csv' | 'ndjson';
export type TraceExportScope = 'visible' | 'filtered';

export interface TraceExportResult {
	spans: SpanDto[];
	truncated: boolean;
}

/** Paginates /api/spans/search for `filter` until exhausted or EXPORT_ROW_CAP is hit. */
export async function fetchAllTracesForExport(
	filter: SpanFilter,
	sort: { sortBy: SpanSortKey; sortAscending: boolean },
	signal?: AbortSignal
): Promise<TraceExportResult> {
	const spans: SpanDto[] = [];
	const seen = new Set<string>();
	let cursor: string | undefined;
	for (;;) {
		const res = await searchSpans({ filter, cursor, pageSize: EXPORT_PAGE_SIZE, ...sort }, signal);
		for (const s of res.spans) {
			const key = `${s.traceId}:${s.spanId}`;
			if (seen.has(key)) continue;
			seen.add(key);
			spans.push(s);
		}
		if (spans.length >= EXPORT_ROW_CAP) {
			return { spans: spans.slice(0, EXPORT_ROW_CAP), truncated: res.nextCursor != null };
		}
		if (!res.nextCursor) return { spans, truncated: false };
		cursor = res.nextCursor;
	}
}

export interface TraceExportColumn {
	id: string;
	/** CSV header / NDJSON key. */
	header: string;
	value: (span: SpanDto) => string | number | boolean | Record<string, string>;
	/** Pre-ticked in the dialog. */
	defaultOn: boolean;
}

export const TRACE_EXPORT_COLUMNS: TraceExportColumn[] = [
	{ id: 'traceId', header: 'TraceId', value: (s) => s.traceId, defaultOn: true },
	{ id: 'spanId', header: 'SpanId', value: (s) => s.spanId, defaultOn: false },
	{ id: 'parentSpanId', header: 'ParentSpanId', value: (s) => s.parentSpanId, defaultOn: false },
	{ id: 'service', header: 'Service', value: (s) => s.serviceName, defaultOn: true },
	{ id: 'name', header: 'Name', value: (s) => s.name, defaultOn: true },
	{ id: 'kind', header: 'Kind', value: (s) => s.kind, defaultOn: false },
	{ id: 'startTime', header: 'StartTime', value: (s) => s.startTime, defaultOn: true },
	{ id: 'endTime', header: 'EndTime', value: (s) => s.endTime, defaultOn: false },
	{ id: 'durationNano', header: 'DurationNano', value: (s) => s.durationNano, defaultOn: true },
	{ id: 'status', header: 'Status', value: (s) => s.statusCode, defaultOn: true },
	{ id: 'statusMessage', header: 'StatusMessage', value: (s) => s.statusMessage, defaultOn: false },
	{ id: 'spanCount', header: 'SpanCount', value: (s) => s.spanCount ?? '', defaultOn: true },
	{ id: 'hasError', header: 'HasError', value: (s) => s.hasError ?? '', defaultOn: true },
	{ id: 'spanAttributes', header: 'SpanAttributes', value: (s) => s.spanAttributes, defaultOn: false },
	{ id: 'resourceAttributes', header: 'ResourceAttributes', value: (s) => s.resourceAttributes, defaultOn: false }
];

export const DEFAULT_TRACE_EXPORT_COLUMNS = TRACE_EXPORT_COLUMNS.filter((c) => c.defaultOn).map((c) => c.id);

function selected(columnIds: readonly string[]): TraceExportColumn[] {
	const set = new Set(columnIds);
	return TRACE_EXPORT_COLUMNS.filter((c) => set.has(c.id));
}

/** Quotes a field if it contains a comma, quote, or newline; doubles any internal quote. */
function csvEscape(value: string): string {
	return /[",\n\r]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}

function csvCell(value: ReturnType<TraceExportColumn['value']>): string {
	return typeof value === 'object' ? JSON.stringify(value) : String(value);
}

export function tracesToCsv(spans: SpanDto[], columnIds: readonly string[]): string {
	const cols = selected(columnIds);
	const lines = [cols.map((c) => c.header), ...spans.map((s) => cols.map((c) => csvCell(c.value(s))))].map((row) =>
		row.map(csvEscape).join(',')
	);
	return lines.join('\r\n') + '\r\n';
}

/** One JSON object per line; attribute columns stay real nested objects (CSV has to stringify them). */
export function tracesToNdjson(spans: SpanDto[], columnIds: readonly string[]): string {
	const cols = selected(columnIds);
	return spans.map((s) => JSON.stringify(Object.fromEntries(cols.map((c) => [c.header, c.value(s)])))).join('\n') + '\n';
}

export function tracesToBlob(spans: SpanDto[], columnIds: readonly string[], format: TraceExportFormat): Blob {
	return format === 'csv'
		? new Blob([tracesToCsv(spans, columnIds)], { type: 'text/csv;charset=utf-8' })
		: new Blob([tracesToNdjson(spans, columnIds)], { type: 'application/x-ndjson;charset=utf-8' });
}

function timestampForFilename(iso: string): string {
	return iso.replace(/[:.]/g, '').replace(/Z$/, '').slice(0, 15);
}

export function traceExportFilename(range: ResolvedTimeRange | null, truncated: boolean, format: TraceExportFormat, scope: TraceExportScope): string {
	const rangePart = range ? `${timestampForFilename(range.from)}_${timestampForFilename(range.to)}` : 'all-time';
	const scopePart = scope === 'visible' ? '_visible-rows' : '';
	const truncatedPart = truncated ? `_first-${EXPORT_ROW_CAP}-rows` : '';
	return `flare-traces_${rangePart}${scopePart}${truncatedPart}.${format}`;
}

/**
 * The trace as currently loaded in the detail page. `truncated`/`partialSpanIds` are kept so
 * a lazily loaded large trace's file says it is incomplete rather than looking whole.
 */
export function traceToJson(trace: TraceDto, partialSpanIds: Iterable<string> = trace.partialSpanIds): string {
	const partial = [...partialSpanIds];
	return JSON.stringify(
		{ traceId: trace.traceId, spanCount: trace.spans.length, truncated: trace.truncated && partial.length > 0, partialSpanIds: partial, spans: trace.spans },
		null,
		2
	);
}

export function traceJsonFilename(traceId: string): string {
	return `flare-trace_${traceId}.json`;
}
