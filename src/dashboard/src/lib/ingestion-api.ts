// Client for Flare.Api's Ingestion page endpoint.
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape. Every type here
// nests a `DateTimeOffset` member somewhere (`IngestionBucketPoint.BucketStart`,
// `IngestionErrorEntryDto.Timestamp`, `IngestionStatsResponse.GeneratedAt`), so all three are
// hand-written (`$lib/memorypack/`); `IngestionSignal`/`IngestionProtocol` convert through
// `$lib/memorypack/enums.ts`'s ordinal↔string adapters, same reasoning `auth-api.ts`
// documents for `UserRole`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders } from './api';
import { ingestionProtocolToString, ingestionSignalToString, type IngestionProtocolName, type IngestionSignalName } from '$lib/memorypack/enums';
import { IngestionStatsResponse as GeneratedIngestionStatsResponse } from '$lib/memorypack/IngestionStatsResponse';
import type { IngestionBucketPoint as GeneratedIngestionBucketPoint } from '$lib/memorypack/IngestionBucketPoint';
import type { IngestionErrorEntryDto as GeneratedIngestionErrorEntryDto } from '$lib/memorypack/IngestionErrorEntryDto';

export type IngestionSignal = IngestionSignalName;
export type IngestionProtocol = IngestionProtocolName;

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

function toIngestionBucketPoint(dto: GeneratedIngestionBucketPoint): IngestionBucketPoint {
	return {
		bucketStart: dto.bucketStart.toISOString(),
		signal: ingestionSignalToString(dto.signal),
		protocol: ingestionProtocolToString(dto.protocol),
		requests: Number(dto.requests),
		records: Number(dto.records),
		bytes: Number(dto.bytes),
		rejected: Number(dto.rejected)
	};
}

function toIngestionErrorEntry(dto: GeneratedIngestionErrorEntryDto): IngestionErrorEntry {
	return {
		timestamp: dto.timestamp.toISOString(),
		signal: dto.signal ?? '',
		protocol: dto.protocol ?? '',
		reason: dto.reason ?? ''
	};
}

export async function getIngestionStats(minutes: number, signal?: AbortSignal): Promise<IngestionStatsResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/ingestion/stats?minutes=${minutes}`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/ingestion/stats failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedIngestionStatsResponse.deserialize(await res.arrayBuffer());
	if (dto == null || dto.totals == null) {
		throw new Error('Empty response body decoding IngestionStatsResponse.');
	}
	return {
		generatedAt: dto.generatedAt.toISOString(),
		minutes: dto.minutes,
		buckets: (dto.buckets ?? []).map((b) => toIngestionBucketPoint(b!)),
		totals: {
			arrivalsPerMinute: Number(dto.totals.arrivalsPerMinute),
			ingestedRecordsPerMinute: Number(dto.totals.ingestedRecordsPerMinute),
			ingestedBytesPerMinute: Number(dto.totals.ingestedBytesPerMinute),
			requestsInWindow: Number(dto.totals.requestsInWindow),
			rejectedInWindow: Number(dto.totals.rejectedInWindow)
		},
		recentErrors: (dto.recentErrors ?? []).map((e) => toIngestionErrorEntry(e!))
	};
}
