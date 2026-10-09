// Client for Flare.Api's Usage page (`GET /api/usage`, admin-only). Plain JSON, not
// MemoryPack - a few small bounded tables, same call `retention-api.ts` makes.

import { API_BASE_URL, apiFetch } from './api';

export type UsageSignal = 'Logs' | 'Traces' | 'Metrics';

export interface UsageSignalSummary {
	signal: UsageSignal;
	events: number;
	compressedBytes: number;
}

export interface UsageDayPoint {
	day: string;
	signal: UsageSignal;
	events: number;
}

export interface UsageServiceRow {
	serviceName: string;
	signal: UsageSignal;
	events: number;
	estimatedBytes: number;
}

export interface UsageAttributeRow {
	signal: UsageSignal;
	scope: string;
	key: string;
	sampledBytes: number;
	sampledOccurrences: number;
}

export interface UsageIngestKeyRow {
	id: string;
	name: string;
	eventsToday: number;
	bytesToday: number;
}

export interface UsageResponse {
	generatedAt: string;
	days: number;
	signals: UsageSignalSummary[];
	daily: UsageDayPoint[];
	services: UsageServiceRow[];
	attributes: UsageAttributeRow[];
	attributeSampleRows: number;
	ingestKeys: UsageIngestKeyRow[];
}

export async function getUsage(days: number, signal?: AbortSignal): Promise<UsageResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/usage?days=${days}`, { signal });
	if (!res.ok) throw new Error(`GET /api/usage failed: ${res.status} ${await res.text().catch(() => '')}`.trim());
	return (await res.json()) as UsageResponse;
}
