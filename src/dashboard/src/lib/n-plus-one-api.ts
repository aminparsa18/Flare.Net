// Client for Flare.Api's N+1 worst-offenders endpoint (`POST /api/traces/n-plus-one`, see
// src/Flare.Api/Endpoints/NPlusOneEndpoints.cs and NPlusOneQueryBuilder.cs). Plain JSON - the
// response is a flat list of scalar rows, so there's no MemoryPack adapter to maintain.

import { API_BASE_URL, apiFetch } from './api';

export interface NPlusOneOffender {
	serviceName: string;
	/** Normalized statement (literals replaced by `?`), or `operation collection` when no query text was recorded. */
	statement: string;
	traceCount: number;
	maxRepeats: number;
	totalRepeats: number;
	totalDurationMs: number;
	exampleTraceId: string;
}

export interface NPlusOneResponse {
	windowMinutes: number;
	minRepeats: number;
	offenders: NPlusOneOffender[];
}

export async function getNPlusOneOffenders(
	windowMinutes: number,
	service: string,
	minRepeats: number | null,
	signal?: AbortSignal
): Promise<NPlusOneResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/traces/n-plus-one`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ windowMinutes, service: service || null, minRepeats }),
		signal
	});
	if (!res.ok) throw new Error(`N+1 query failed: ${res.status} ${res.statusText}`);
	return (await res.json()) as NPlusOneResponse;
}
