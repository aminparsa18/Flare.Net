// Client for Flare.Api's per-statement database view (`POST /api/traces/db-statements`, see
// src/Flare.Api/Endpoints/DbStatementEndpoints.cs and DbStatementQueryBuilder.cs). Plain JSON -
// the response is a flat list of scalar rows, so there's no MemoryPack adapter to maintain.

import { API_BASE_URL, apiFetch } from './api';

export type DbStatementSort = 'totalTime' | 'p95' | 'calls' | 'errors';

export interface DbStatementRow {
	serviceName: string;
	dbSystem: string;
	/** Normalized statement (literals replaced by `?`), or `operation collection` when no query text was recorded. */
	statement: string;
	callCount: number;
	errorCount: number;
	callsPerMinute: number;
	avgDurationMs: number;
	p95DurationMs: number;
	totalDurationMs: number;
	exampleTraceId: string;
}

export interface DbStatementResponse {
	windowMinutes: number;
	sortBy: DbStatementSort;
	statements: DbStatementRow[];
}

export async function getDbStatements(
	windowMinutes: number,
	service: string,
	dbSystem: string,
	sortBy: DbStatementSort,
	signal?: AbortSignal
): Promise<DbStatementResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/traces/db-statements`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ windowMinutes, service: service || null, dbSystem: dbSystem || null, sortBy }),
		signal
	});
	if (!res.ok) throw new Error(`Database statements query failed: ${res.status} ${res.statusText}`);
	return (await res.json()) as DbStatementResponse;
}
