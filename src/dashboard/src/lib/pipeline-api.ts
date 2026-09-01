// Client for Flare.Api's pipeline-health endpoint (Planning.md v10) - the Ingestion
// page's "is the buffered pipeline keeping up" section, alongside ingestion-api.ts's
// "is data arriving" one (v8).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape.
// `PipelineStreamHealth`/`PipelineServiceEntry` have no DateTimeOffset/JsonElement member
// and use real generated classes; `PipelineFlushHealth` (DateTimeOffset?),
// `PipelineServiceBreakdown` (an `IReadOnlyList<T>` member - see that hand-written file's
// header comment), and the top-level `PipelineStatsResponse` are hand-written
// (`$lib/memorypack/`). `signal` converts through `$lib/memorypack/enums.ts`'s
// `ingestionSignalToString`, same enum `ingestion-api.ts` exports as `IngestionSignal`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders } from './api';
import type { IngestionSignal } from './ingestion-api';
import { ingestionSignalToString } from '$lib/memorypack/enums';
import { PipelineStatsResponse as GeneratedPipelineStatsResponse } from '$lib/memorypack/PipelineStatsResponse';
import type { PipelineStreamHealth as GeneratedPipelineStreamHealth } from '$lib/generated/memorypack/PipelineStreamHealth.js';
import type { PipelineFlushHealth as GeneratedPipelineFlushHealth } from '$lib/memorypack/PipelineFlushHealth';
import type { PipelineServiceBreakdown as GeneratedPipelineServiceBreakdown } from '$lib/memorypack/PipelineServiceBreakdown';
import type { PipelineServiceEntry as GeneratedPipelineServiceEntry } from '$lib/generated/memorypack/PipelineServiceEntry.js';

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
	// (IngestedAt - event time) averaged across this service's records in the window
	// (see ADR-0014) - positive = typically arrives claiming a past time relative to
	// receipt (expected: latency), negative = claims a future time (this service's
	// clock is ahead of the server's).
	averageClockSkewMs: number;
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

function toPipelineStreamHealth(dto: GeneratedPipelineStreamHealth): PipelineStreamHealth {
	return {
		signal: ingestionSignalToString(dto.signal),
		streamKey: dto.streamKey ?? '',
		available: dto.available,
		length: Number(dto.length),
		capacity: Number(dto.capacity),
		lag: dto.lag == null ? null : Number(dto.lag),
		pendingCount: Number(dto.pendingCount),
		consumers: dto.consumers,
		oldestPendingAgeSeconds: dto.oldestPendingAgeSeconds
	};
}

function toPipelineFlushHealth(dto: GeneratedPipelineFlushHealth): PipelineFlushHealth {
	return {
		signal: ingestionSignalToString(dto.signal),
		lastFlushAt: dto.lastFlushAt?.toISOString() ?? null,
		lastBatchSize: dto.lastBatchSize == null ? null : Number(dto.lastBatchSize),
		lastErrorAt: dto.lastErrorAt?.toISOString() ?? null,
		lastError: dto.lastError,
		consecutiveErrors: Number(dto.consecutiveErrors)
	};
}

function toPipelineServiceEntry(dto: GeneratedPipelineServiceEntry): PipelineServiceEntry {
	return {
		serviceName: dto.serviceName ?? '',
		records: Number(dto.records),
		bytes: Number(dto.bytes),
		averageClockSkewMs: dto.averageClockSkewMs
	};
}

function toPipelineServiceBreakdown(dto: GeneratedPipelineServiceBreakdown): PipelineServiceBreakdown {
	return {
		signal: ingestionSignalToString(dto.signal),
		topServices: (dto.topServices ?? []).map((e) => toPipelineServiceEntry(e!)),
		otherServiceCount: Number(dto.otherServiceCount),
		otherRecords: Number(dto.otherRecords),
		otherBytes: Number(dto.otherBytes)
	};
}

export async function getPipelineStats(minutes: number, signal?: AbortSignal): Promise<PipelineStatsResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/ingestion/pipeline?minutes=${minutes}`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/ingestion/pipeline failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedPipelineStatsResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding PipelineStatsResponse.');
	}
	return {
		generatedAt: dto.generatedAt.toISOString(),
		streams: (dto.streams ?? []).map((s) => toPipelineStreamHealth(s!)),
		flushWorkers: (dto.flushWorkers ?? []).map((f) => toPipelineFlushHealth(f!)),
		serviceBreakdowns: (dto.serviceBreakdowns ?? []).map((b) => toPipelineServiceBreakdown(b!))
	};
}
