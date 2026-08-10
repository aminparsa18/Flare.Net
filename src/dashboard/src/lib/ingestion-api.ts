// Client for Flare.Api's Ingestion page endpoint. Field names/casing/enum values are a
// hand-mirror of src/Flare.Api/Model/IngestionModels.cs + Json/IngestionJsonContext.cs -
// same camelCase-properties/PascalCase-string-enum-values convention `api.ts` documents
// for LogsJsonContext. Keep in sync with those files by hand.

import { API_BASE_URL, apiFetch } from './api';

export type IngestionSignal = 'Logs' | 'Traces' | 'Metrics';
export type IngestionProtocol = 'Grpc' | 'Http';

export interface IngestionBucketPoint {
	bucketStart: string;
	signal: IngestionSignal;
	protocol: IngestionProtocol;
	requests: number;
	records: number;
	bytes: number;
	rejected: number;
}

export interface IngestionErrorEntry {
	timestamp: string;
	signal: string;
	protocol: string;
	reason: string;
}

export interface IngestionStatsTotals {
	arrivalsPerMinute: number;
	ingestedRecordsPerMinute: number;
	ingestedBytesPerMinute: number;
	requestsInWindow: number;
	rejectedInWindow: number;
}

export interface IngestionStatsResponse {
	generatedAt: string;
	minutes: number;
	buckets: IngestionBucketPoint[];
	totals: IngestionStatsTotals;
	recentErrors: IngestionErrorEntry[];
}

export async function getIngestionStats(minutes: number, signal?: AbortSignal): Promise<IngestionStatsResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/ingestion/stats?minutes=${minutes}`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/ingestion/stats failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
