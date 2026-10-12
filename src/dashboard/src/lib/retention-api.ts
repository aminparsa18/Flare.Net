// Client for Flare.Api's data retention (`/api/retention`, ADR-0143/0144/0145).
//
// Plain JSON, not MemoryPack - a handful of small config rows, same call `log-metrics-api.ts`
// makes. Days: 0 means "keep forever" (and, for `coldAfterDays`, "no tiering").

import { API_BASE_URL, apiFetch } from './api';

export interface RetentionRule {
	attribute: string;
	value: string;
	days: number;
}

export type RetentionActualState = 'none' | 'days' | 'custom';
export type RetentionStatus = 'pending' | 'success' | 'failed';

export interface SignalRetention {
	signal: string;
	tables: string[];
	actualDays: number | null;
	actualColdAfterDays: number | null;
	actualState: RetentionActualState;
	expectedDays: number | null;
	expectedColdAfterDays: number | null;
	actualRules: RetentionRule[];
	expectedRules: RetentionRule[];
	status: RetentionStatus | null;
	error: string | null;
	transactionId: string | null;
	updatedAt: string | null;
}

export interface StorageDisk {
	name: string;
	type: 'Local' | 'ObjectStorage';
	freeBytes: number;
	totalBytes: number;
}

export interface RetentionResponse {
	signals: SignalRetention[];
	coldStorage: { available: boolean; disks: StorageDisk[] };
}

/** One signal's change, as `PUT /api/retention` takes it. */
export interface RetentionChange {
	signal: string;
	days: number;
	coldAfterDays: number;
	rules: RetentionRule[];
}

/** Mirrors `RetentionSql.MaxDays` and `RetentionRuleSql`'s per-signal cap. */
export const MAX_RETENTION_DAYS = 18250;
export const MAX_RETENTION_RULES = 20;

const URL = `${API_BASE_URL}/api/retention`;

async function failure(res: Response, what: string): Promise<Error> {
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// Not a problem-details body - fall back to the status line.
	}
	return new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

export async function getRetention(signal?: AbortSignal): Promise<RetentionResponse> {
	const res = await apiFetch(URL, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/retention');
	return (await res.json()) as RetentionResponse;
}

/** Applies one signal's retention. Resolves once the API has accepted it (202); ClickHouse applies it asynchronously, so poll `getRetention` for `status`. */
export async function setRetention(change: RetentionChange): Promise<void> {
	const body = {
		signals: { [change.signal]: change.days },
		coldAfterDays: { [change.signal]: change.coldAfterDays },
		rules: { [change.signal]: change.rules }
	};
	const res = await apiFetch(URL, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
	if (!res.ok) throw await failure(res, 'PUT /api/retention');
}
