// Client for Flare.Api's pipeline-health endpoint (Planning.md v10) - the Ingestion
// page's "is the buffered pipeline keeping up" section, alongside ingestion-api.ts's
// "is data arriving" one (v8). Field names/casing/enum values hand-mirror
// src/Flare.Api/Model/PipelineModels.cs + Json/PipelineJsonContext.cs, same convention
// ingestion-api.ts documents.

import { API_BASE_URL, apiFetch } from './api';
import type { IngestionSignal } from './ingestion-api';

export interface PipelineStreamHealth {
	signal: IngestionSignal;
	streamKey: string;
	available: boolean;
	length: number;
	capacity: number;
	lag: number | null;
	pendingCount: number;
	consumers: number;
	oldestPendingAgeSeconds: number | null;
}

export interface PipelineFlushHealth {
	signal: IngestionSignal;
	lastFlushAt: string | null;
	lastBatchSize: number | null;
	lastErrorAt: string | null;
	lastError: string | null;
	consecutiveErrors: number;
}

export interface PipelineServiceEntry {
	serviceName: string;
	records: number;
	bytes: number;
}

export interface PipelineServiceBreakdown {
	signal: IngestionSignal;
	topServices: PipelineServiceEntry[];
	otherServiceCount: number;
	otherRecords: number;
	otherBytes: number;
}

export interface PipelineStatsResponse {
	generatedAt: string;
	streams: PipelineStreamHealth[];
	flushWorkers: PipelineFlushHealth[];
	serviceBreakdowns: PipelineServiceBreakdown[];
}

export async function getPipelineStats(minutes: number, signal?: AbortSignal): Promise<PipelineStatsResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/ingestion/pipeline?minutes=${minutes}`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/ingestion/pipeline failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
