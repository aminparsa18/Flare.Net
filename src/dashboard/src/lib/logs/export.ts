// Client-side CSV/XLSX/JSON/XML export for the Logs Explorer table. No backend endpoint.
// Two distinct scopes, both ending up as the same LogEventDto[] -> format pipeline:
//  - "visible": exactly what's currently rendered in LogsExplorerState.events (a
//    UI-scrollback cap - LIVE_CAP/PAGINATION_CAP - not "everything matching the filter").
//    No fetch, instant.
//  - "filtered": the full filtered result set, paginated via the existing
//    /api/logs/search (bounded by EXPORT_ROW_CAP below).
// ExportDialog.svelte asks the user which one they want rather than assuming - exporting
// silently more (or less) than what's on screen is surprising either way.
//
// CSV needs no library (RFC4180 escaping is a few lines); XLSX is a zip-based OOXML
// container that isn't worth hand-rolling, hence the one dependency (SheetJS).
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

export type ExportFormat = 'csv' | 'xlsx' | 'json' | 'xml';

/** "visible" = exactly the rows currently rendered in the table; "filtered" = the full filtered result set (paginated fetch). */
export type ExportScope = 'visible' | 'filtered';

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

/** Same field set as HEADER/eventToRow, but as a JSON-native object - logAttributes stays a
 *  real nested object here rather than the double-stringified LogAttributesJson column CSV/
 *  XLSX need (flat rows can't hold a nested value). */
function eventToJsonObject(event: LogEventDto) {
	return {
		eventId: event.eventId,
		timestamp: event.timestamp,
		severity: event.severityText,
		severityNumber: event.severityNumber,
		service: event.serviceName,
		eventName: event.eventName,
		message: event.body,
		traceId: event.traceId,
		spanId: event.spanId,
		logAttributes: event.logAttributes
	};
}

export function eventsToJson(events: LogEventDto[]): string {
	return JSON.stringify(events.map(eventToJsonObject), null, 2);
}

/** Escapes text content for use inside an XML element - `&`/`<`/`>` are the only characters that are ever unsafe there. */
function xmlEscapeText(value: string): string {
	return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

/** Text-content escaping plus `"`, for use inside a double-quoted attribute value. */
function xmlEscapeAttr(value: string): string {
	return xmlEscapeText(value).replace(/"/g, '&quot;');
}

function eventToXmlElement(event: LogEventDto): string {
	const attributeEntries = Object.entries(event.logAttributes);
	const attributesXml = attributeEntries.length
		? `    <LogAttributes>\n${attributeEntries
				.map(([key, value]) => `      <Attribute key="${xmlEscapeAttr(key)}">${xmlEscapeText(value)}</Attribute>`)
				.join('\n')}\n    </LogAttributes>`
		: '    <LogAttributes />';
	return [
		'  <Log>',
		`    <EventId>${xmlEscapeText(event.eventId)}</EventId>`,
		`    <Timestamp>${xmlEscapeText(event.timestamp)}</Timestamp>`,
		`    <Severity>${xmlEscapeText(event.severityText)}</Severity>`,
		`    <SeverityNumber>${event.severityNumber}</SeverityNumber>`,
		`    <Service>${xmlEscapeText(event.serviceName)}</Service>`,
		`    <EventName>${xmlEscapeText(event.eventName)}</EventName>`,
		`    <Message>${xmlEscapeText(event.body)}</Message>`,
		`    <TraceId>${xmlEscapeText(event.traceId)}</TraceId>`,
		`    <SpanId>${xmlEscapeText(event.spanId)}</SpanId>`,
		attributesXml,
		'  </Log>'
	].join('\n');
}

export function eventsToXml(events: LogEventDto[]): string {
	return `<?xml version="1.0" encoding="UTF-8"?>\n<Logs>\n${events.map(eventToXmlElement).join('\n')}\n</Logs>\n`;
}

/** Dispatches to the right writer for `format` and wraps the result in a Blob with the right MIME type - the one place ExportDialog.svelte needs to know about, rather than growing a per-format ternary there as formats are added. */
export function eventsToBlob(events: LogEventDto[], format: ExportFormat): Blob {
	switch (format) {
		case 'csv':
			return new Blob([eventsToCsv(events)], { type: 'text/csv;charset=utf-8' });
		case 'xlsx':
			return eventsToXlsxBlob(events);
		case 'json':
			return new Blob([eventsToJson(events)], { type: 'application/json;charset=utf-8' });
		case 'xml':
			return new Blob([eventsToXml(events)], { type: 'application/xml;charset=utf-8' });
	}
}

/** yyyy-MM-ddTHHmmss, filesystem-safe (no colons). */
function timestampForFilename(iso: string): string {
	return iso.replace(/[:.]/g, '').replace(/Z$/, '').slice(0, 15);
}

/** Bakes the applied range, scope, and truncation into the filename so the artifact self-documents even if a truncation alert was dismissed unread. */
export function exportFilename(
	range: ResolvedTimeRange | null,
	truncated: boolean,
	format: ExportFormat,
	scope: ExportScope
): string {
	const rangePart = range ? `${timestampForFilename(range.from)}_${timestampForFilename(range.to)}` : 'all-time';
	const scopePart = scope === 'visible' ? '_visible-rows' : '';
	const truncatedPart = truncated ? `_first-${EXPORT_ROW_CAP}-rows` : '';
	return `flare-logs_${rangePart}${scopePart}${truncatedPart}.${format}`;
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
