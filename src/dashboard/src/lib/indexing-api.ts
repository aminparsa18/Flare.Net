// Client for Flare.Api's Indexing page endpoint.
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape.
// `TableStorageInfo`/`SkipIndexInfo`/`DiskUsageInfo`/`QueryPerformanceInfo`/`ClusterNodeInfo`
// have no DateTimeOffset/JsonElement/IReadOnlyList member and use real generated classes;
// `StorageGrowthPoint` (DateTimeOffset), `IndexingStatsResponse`, and `ClusterStatusResponse`
// (both IReadOnlyList-of-object members - see `PipelineServiceBreakdown.ts`'s header comment
// for why that alone blocks `[GenerateTypeScript]`) are hand-written (`$lib/memorypack/`).

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders, type AttributeBag } from './api';
import { IndexingStatsResponse as GeneratedIndexingStatsResponse } from '$lib/memorypack/IndexingStatsResponse';
import { ClusterStatusResponse as GeneratedClusterStatusResponse } from '$lib/memorypack/ClusterStatusResponse';
import type { TableStorageInfo as GeneratedTableStorageInfo } from '$lib/generated/memorypack/TableStorageInfo.js';
import type { SkipIndexInfo as GeneratedSkipIndexInfo } from '$lib/generated/memorypack/SkipIndexInfo.js';
import type { StorageGrowthPoint as GeneratedStorageGrowthPoint } from '$lib/memorypack/StorageGrowthPoint';
import type { ClusterNodeInfo as GeneratedClusterNodeInfo } from '$lib/generated/memorypack/ClusterNodeInfo.js';
import { PromotedAttributesResponse as GeneratedPromotedAttributesResponse } from '$lib/memorypack/PromotedAttributesResponse';
import { PromoteAttributeRequest as GeneratedPromoteAttributeRequest } from '$lib/generated/memorypack/PromoteAttributeRequest.js';
import { attributeBagFromString, attributeBagToString } from '$lib/memorypack/enums';

export interface TableStorageInfo {
	tableName: string;
	engine: string;
	sortingKey: string;
	rows: number;
	activeParts: number;
	compressedBytes: number;
	uncompressedBytes: number;
}

export interface SkipIndexInfo {
	tableName: string;
	indexName: string;
	type: string;
	expression: string;
	granularity: number;
	compressedBytes: number;
	uncompressedBytes: number;
}

export interface StorageGrowthPoint {
	day: string;
	tableName: string;
	bytes: number;
	rows: number;
}

export interface DiskUsageInfo {
	available: boolean;
	totalBytes: number;
	freeBytes: number;
}

export interface QueryPerformanceInfo {
	available: boolean;
	p50Ms: number | null;
	p95Ms: number | null;
	p99Ms: number | null;
	slowQueryCount: number;
	sampleCount: number;
	windowMinutes: number;
	slowQueryThresholdMs: number;
}

export interface IndexingStatsResponse {
	generatedAt: string;
	tables: TableStorageInfo[];
	skipIndexes: SkipIndexInfo[];
	growth: StorageGrowthPoint[];
	growthAvailable: boolean;
	diskUsage: DiskUsageInfo;
	queryPerformance: QueryPerformanceInfo;
}

function toTableStorageInfo(dto: GeneratedTableStorageInfo): TableStorageInfo {
	return {
		tableName: dto.tableName ?? '',
		engine: dto.engine ?? '',
		sortingKey: dto.sortingKey ?? '',
		rows: Number(dto.rows),
		activeParts: Number(dto.activeParts),
		compressedBytes: Number(dto.compressedBytes),
		uncompressedBytes: Number(dto.uncompressedBytes)
	};
}

function toSkipIndexInfo(dto: GeneratedSkipIndexInfo): SkipIndexInfo {
	return {
		tableName: dto.tableName ?? '',
		indexName: dto.indexName ?? '',
		type: dto.type ?? '',
		expression: dto.expression ?? '',
		granularity: Number(dto.granularity),
		compressedBytes: Number(dto.compressedBytes),
		uncompressedBytes: Number(dto.uncompressedBytes)
	};
}

function toStorageGrowthPoint(dto: GeneratedStorageGrowthPoint): StorageGrowthPoint {
	return {
		day: dto.day.toISOString(),
		tableName: dto.tableName ?? '',
		bytes: Number(dto.bytes),
		rows: Number(dto.rows)
	};
}

export async function getIndexingStats(signal?: AbortSignal): Promise<IndexingStatsResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/indexing/stats`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/indexing/stats failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedIndexingStatsResponse.deserialize(await res.arrayBuffer());
	if (dto == null || dto.diskUsage == null || dto.queryPerformance == null) {
		throw new Error('Empty response body decoding IndexingStatsResponse.');
	}
	return {
		generatedAt: dto.generatedAt.toISOString(),
		tables: (dto.tables ?? []).map((t) => toTableStorageInfo(t!)),
		skipIndexes: (dto.skipIndexes ?? []).map((s) => toSkipIndexInfo(s!)),
		growth: (dto.growth ?? []).map((g) => toStorageGrowthPoint(g!)),
		growthAvailable: dto.growthAvailable,
		diskUsage: {
			available: dto.diskUsage.available,
			totalBytes: Number(dto.diskUsage.totalBytes),
			freeBytes: Number(dto.diskUsage.freeBytes)
		},
		queryPerformance: {
			available: dto.queryPerformance.available,
			p50Ms: dto.queryPerformance.p50Ms,
			p95Ms: dto.queryPerformance.p95Ms,
			p99Ms: dto.queryPerformance.p99Ms,
			slowQueryCount: Number(dto.queryPerformance.slowQueryCount),
			sampleCount: Number(dto.queryPerformance.sampleCount),
			windowMinutes: dto.queryPerformance.windowMinutes,
			slowQueryThresholdMs: dto.queryPerformance.slowQueryThresholdMs
		}
	};
}

export interface ClusterNodeInfo {
	shardNum: number;
	replicaNum: number;
	hostName: string;
	port: number;
	isLocal: boolean;
	errorsCount: number;
	estimatedRecoveryTimeSeconds: number;
	// Both 0 when the parent response's replicationInfoAvailable is false - that's a
	// "couldn't read it," not a real "caught up" reading, so check the flag first rather
	// than trusting a bare 0 here.
	replicationQueueSize: number;
	replicationLagSeconds: number;
}

export interface ClusterStatusResponse {
	clusterModeEnabled: boolean;
	sharedPatternStoreEnabled: boolean;
	replicationInfoAvailable: boolean;
	nodes: ClusterNodeInfo[];
}

function toClusterNodeInfo(dto: GeneratedClusterNodeInfo): ClusterNodeInfo {
	return {
		shardNum: dto.shardNum,
		replicaNum: dto.replicaNum,
		hostName: dto.hostName ?? '',
		port: dto.port,
		isLocal: dto.isLocal,
		errorsCount: Number(dto.errorsCount),
		estimatedRecoveryTimeSeconds: Number(dto.estimatedRecoveryTimeSeconds),
		replicationQueueSize: Number(dto.replicationQueueSize),
		replicationLagSeconds: Number(dto.replicationLagSeconds)
	};
}

export async function getClusterStatus(signal?: AbortSignal): Promise<ClusterStatusResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/indexing/cluster`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/indexing/cluster failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedClusterStatusResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding ClusterStatusResponse.');
	}
	return {
		clusterModeEnabled: dto.clusterModeEnabled,
		sharedPatternStoreEnabled: dto.sharedPatternStoreEnabled,
		replicationInfoAvailable: dto.replicationInfoAvailable,
		nodes: (dto.nodes ?? []).map((n) => toClusterNodeInfo(n!))
	};
}

// Promoted attribute columns (ADR-0062, ADR-0063) - a log or span attribute key promoted to
// its own MATERIALIZED column + skip index, which every filter on that key then reads
// instead of the whole attribute map. Listing is open to any signed-in user; promote/demote
// are Admin-only server-side.

/** Which table a promoted column lives on. With `table: 'Spans'`, bag `'Log'` means `SpanAttributes`. */
export type PromotedAttributeTable = 'Logs' | 'Spans';

const PROMOTED_TABLES: PromotedAttributeTable[] = ['Logs', 'Spans'];

export interface PromotedAttribute {
	table: PromotedAttributeTable;
	bag: AttributeBag;
	key: string;
	columnName: string;
	indexName: string;
	/** A MATERIALIZE COLUMN/INDEX mutation for this column is still running on older data. */
	backfilling: boolean;
}

export interface PromotedAttributesResponse {
	attributes: PromotedAttribute[];
	maxPromotedAttributes: number;
}

export async function getPromotedAttributes(signal?: AbortSignal): Promise<PromotedAttributesResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/indexing/promoted-attributes`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/indexing/promoted-attributes failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedPromotedAttributesResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding PromotedAttributesResponse.');
	}
	return {
		attributes: (dto.attributes ?? []).map((a) => ({
			table: PROMOTED_TABLES[a!.table] ?? 'Logs',
			bag: attributeBagToString(a!.bag),
			key: a!.key ?? '',
			columnName: a!.columnName ?? '',
			indexName: a!.indexName ?? '',
			backfilling: a!.backfilling
		})),
		maxPromotedAttributes: dto.maxPromotedAttributes
	};
}

/** Throws with the API's problem `detail` (e.g. "already promoted", invalid key) as the message. */
export async function promoteAttribute(table: PromotedAttributeTable, bag: AttributeBag, key: string, backfill: boolean): Promise<void> {
	const dto = new GeneratedPromoteAttributeRequest();
	dto.bag = attributeBagFromString(bag);
	dto.key = key;
	dto.backfill = backfill;
	dto.table = PROMOTED_TABLES.indexOf(table);
	const res = await apiFetch(`${API_BASE_URL}/api/indexing/promoted-attributes`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedPromoteAttributeRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(await problemDetail(res, 'POST /api/indexing/promoted-attributes'));
	}
}

export async function demoteAttribute(table: PromotedAttributeTable, columnName: string): Promise<void> {
	const url = `${API_BASE_URL}/api/indexing/promoted-attributes/${encodeURIComponent(columnName)}?table=${table.toLowerCase()}`;
	const res = await apiFetch(url, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(await problemDetail(res, `DELETE /api/indexing/promoted-attributes/${columnName}`));
	}
}

async function problemDetail(res: Response, what: string): Promise<string> {
	try {
		const body = (await res.json()) as { detail?: string };
		if (body.detail) return body.detail;
	} catch {
		// Not a problem+json body - fall through to the status line.
	}
	return `${what} failed: ${res.status} ${res.statusText}`;
}
