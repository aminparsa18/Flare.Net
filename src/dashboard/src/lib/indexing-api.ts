// Client for Flare.Api's Indexing page endpoint.
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape.
// `TableStorageInfo`/`SkipIndexInfo`/`DiskUsageInfo`/`QueryPerformanceInfo`/`ClusterNodeInfo`
// have no DateTimeOffset/JsonElement/IReadOnlyList member and use real generated classes;
// `StorageGrowthPoint` (DateTimeOffset), `IndexingStatsResponse`, and `ClusterStatusResponse`
// (both IReadOnlyList-of-object members - see `PipelineServiceBreakdown.ts`'s header comment
// for why that alone blocks `[GenerateTypeScript]`) are hand-written (`$lib/memorypack/`).

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders } from './api';
import { IndexingStatsResponse as GeneratedIndexingStatsResponse } from '$lib/memorypack/IndexingStatsResponse';
import { ClusterStatusResponse as GeneratedClusterStatusResponse } from '$lib/memorypack/ClusterStatusResponse';
import type { TableStorageInfo as GeneratedTableStorageInfo } from '$lib/generated/memorypack/TableStorageInfo.js';
import type { SkipIndexInfo as GeneratedSkipIndexInfo } from '$lib/generated/memorypack/SkipIndexInfo.js';
import type { StorageGrowthPoint as GeneratedStorageGrowthPoint } from '$lib/memorypack/StorageGrowthPoint';
import type { ClusterNodeInfo as GeneratedClusterNodeInfo } from '$lib/generated/memorypack/ClusterNodeInfo.js';

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
