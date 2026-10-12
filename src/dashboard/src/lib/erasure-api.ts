// Client for Flare.Api's data-subject erasure and export (`/api/erasure`, ADR-0198). Admin-only.
//
// Plain JSON, same as `retention-api.ts`. An erasure is accepted (202) and then applied by ClickHouse
// in the background, so the page polls `getErasure` for per-table progress.

import { API_BASE_URL, apiFetch } from './api';

export interface ErasureIdentifier {
	key: string;
	value: string;
}

export interface ErasureTableCount {
	table: string;
	rows: number;
}

export interface ErasurePreview {
	tables: ErasureTableCount[];
	totalRows: number;
	sessionCount: number;
	sessionsTruncated: boolean;
}

export type ErasureStatus = 'pending' | 'running' | 'success' | 'failed';

export interface ErasureTableProgress {
	table: string;
	rows: number;
	status: ErasureStatus;
	partsToDo: number;
	error: string | null;
}

export interface ErasureOperation {
	transactionId: string;
	status: ErasureStatus;
	error: string | null;
	identifierKeys: string[];
	subjectHash: string;
	requestedBy: string;
	requestedAt: string;
	updatedAt: string;
	tables: ErasureTableProgress[];
}

/** Attribute keys most telemetry carries a person under; offered as suggestions, any key is accepted. */
export const COMMON_IDENTIFIER_KEYS = ['user.id', 'enduser.id', 'user.email', 'client.address', 'session.id'];

const URL = `${API_BASE_URL}/api/erasure`;

async function failure(res: Response, what: string): Promise<Error> {
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// Not a problem-details body - fall back to the status line.
	}
	return new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

function post(path: string, body: unknown): Promise<Response> {
	return apiFetch(`${URL}${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
}

export async function previewErasure(identifiers: ErasureIdentifier[]): Promise<ErasurePreview> {
	const res = await post('/preview', { identifiers });
	if (!res.ok) throw await failure(res, 'POST /api/erasure/preview');
	return (await res.json()) as ErasurePreview;
}

/** Starts an erasure. `confirm` must equal the first identifier's value. Resolves with the transaction id once the API has accepted it. */
export async function startErasure(identifiers: ErasureIdentifier[], confirm: string): Promise<string> {
	const res = await post('', { identifiers, confirm });
	if (!res.ok) throw await failure(res, 'POST /api/erasure');
	return ((await res.json()) as { transactionId: string }).transactionId;
}

/** The subject's rows as NDJSON, one `{"table","row"}` line each. */
export async function exportSubject(identifiers: ErasureIdentifier[]): Promise<Blob> {
	const res = await post('/export', { identifiers });
	if (!res.ok) throw await failure(res, 'POST /api/erasure/export');
	return await res.blob();
}

export async function getErasures(signal?: AbortSignal): Promise<ErasureOperation[]> {
	const res = await apiFetch(URL, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/erasure');
	return ((await res.json()) as { operations: ErasureOperation[] }).operations;
}
