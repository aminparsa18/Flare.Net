// Client-side CSV/XLSX export for the Logs Explorer table. No backend endpoint - fetches
// the full filtered result set via the existing /api/logs/search pagination, then hands
// the same rows to either a hand-rolled CSV writer or SheetJS (xlsx) for a real .xlsx
// workbook. CSV needs no library (RFC4180 escaping is a few lines); XLSX is a zip-based
// OOXML container that isn't worth hand-rolling, hence the one dependency.
//
// Deliberately paginates the *full* filtered result set rather than exporting only
// whatever's currently loaded in LogsExplorerState.events - `events` is a UI-scrollback
// cap (LIVE_CAP/PAGINATION_CAP), not "everything matching the filter", and an export's
// whole point is a complete, self-contained artifact.
//
// The npm-registry `xlsx` package (SheetJS) stalled at 0.18.5 with unpatched high-severity
// CVEs (prototype pollution, ReDoS) - fixes are only published via SheetJS's own CDN, so
// package.json pins `https://cdn.sheetjs.com/xlsx-0.20.3/xlsx-0.20.3.tgz` instead of a
// bare npm version. We only ever *write* a workbook from our own trusted API response here
// (never parse an untrusted file), which is the lower-risk side of those CVEs regardless.

import { searchLogs, type LogEventDto, type LogFilter } from '$lib/api';
import type { ResolvedTimeRange } from './time-range';
import * as XLSX from 'xlsx';

/** Backend's own LogSearchQueryBuilder max PageSize - using it minimizes round trips. */
const EXPORT_PAGE_SIZE = 1000;

/**
 * Hard cap on exported rows. Cursor pagination can't be parallelized (each page's cursor
 * depends on the previous page), so an unbounded export is an unbounded number of
 * sequential round trips - this bounds worst case to 25 requests and a few MB of output,
 * same "cap it, don't silently hang" instinct as PAGINATION_CAP/LIVE_CAP in state.svelte.ts.
 */
export const EXPORT_ROW_CAP = 25_000;

export type ExportFormat = 'csv' | 'xlsx';

export interface ExportResult {
	events: LogEventDto[];
	truncated: boolean;
}

/** Paginates /api/logs/search for `filter` until exhausted or EXPORT_ROW_CAP is hit. */
export async function fetchAllForExport(filter: LogFilter, signal?: AbortSignal): Promise<ExportResult> {
	const events: LogEventDto[] = [];
	let cursor: string | undefined;
	for (;;) {
		const res = await searchLogs({ filter, cursor, pageSize: EXPORT_PAGE_SIZE }, signal);
		events.push(...res.events);
		if (events.length >= EXPORT_ROW_CAP) {
			return { events: events.slice(0, EXPORT_ROW_CAP), truncated: res.nextCursor != null };
		}
		if (!res.nextCursor) return { events, truncated: false };
		cursor = res.nextCursor;
	}
}

// Support-bundle framing: event identity, timing, severity, correlation, message,
// structured payload. Deliberately excludes observedTimestamp/traceFlags/*SchemaUrl/
// scopeName/scopeVersion (low-signal OTel plumbing), resourceAttributes/scopeAttributes
// (per-service/per-scope, mostly redundant with Service), and patternId/patternTemplate
// (internal drill-down state, not exported data) - matches LogRow.svelte's own columns
// plus the correlation ids and attribute bag a support bundle actually needs.
const HEADER = [
	'EventId',
	'Timestamp',
	'Severity',
	'SeverityNumber',
	'Service',
	'EventName',
	'Message',
	'TraceId',
	'SpanId',
	'LogAttributesJson'
];

function eventToRow(event: LogEventDto): string[] {
	return [
		event.eventId,
		event.timestamp,
		event.severityText,
		String(event.severityNumber),
		event.serviceName,
		event.eventName,
		event.body,
		event.traceId,
		event.spanId,
		JSON.stringify(event.logAttributes)
	];
}

/** Quotes a field if it contains a comma, quote, or newline; doubles any internal quote. */
function csvEscape(value: string): string {
	if (/[",\n\r]/.test(value)) {
		return `"${value.replace(/"/g, '""')}"`;
	}
	return value;
}

export function eventsToCsv(events: LogEventDto[]): string {
	const lines = [HEADER, ...events.map(eventToRow)].map((row) => row.map(csvEscape).join(','));
	return lines.join('\r\n') + '\r\n';
}

export function eventsToXlsxBlob(events: LogEventDto[]): Blob {
	const rows = [HEADER, ...events.map(eventToRow)];
	const sheet = XLSX.utils.aoa_to_sheet(rows);
	const workbook = XLSX.utils.book_new();
	XLSX.utils.book_append_sheet(workbook, sheet, 'Logs');
	const bytes = XLSX.write(workbook, { bookType: 'xlsx', type: 'array' }) as ArrayBuffer;
	return new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
}

/** yyyy-MM-ddTHHmmss, filesystem-safe (no colons). */
function timestampForFilename(iso: string): string {
	return iso.replace(/[:.]/g, '').replace(/Z$/, '').slice(0, 15);
}

/** Bakes the applied range and truncation into the filename so the artifact self-documents even if a truncation alert was dismissed unread. */
export function exportFilename(range: ResolvedTimeRange | null, truncated: boolean, format: ExportFormat): string {
	const rangePart = range ? `${timestampForFilename(range.from)}_${timestampForFilename(range.to)}` : 'all-time';
	const truncatedPart = truncated ? `_first-${EXPORT_ROW_CAP}-rows` : '';
	return `flare-logs_${rangePart}${truncatedPart}.${format}`;
}

export function downloadBlob(blob: Blob, filename: string): void {
	const url = URL.createObjectURL(blob);
	try {
		const anchor = document.createElement('a');
		anchor.href = url;
		anchor.download = filename;
		anchor.click();
	} finally {
		URL.revokeObjectURL(url);
	}
}
