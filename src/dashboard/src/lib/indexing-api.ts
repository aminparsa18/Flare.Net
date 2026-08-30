// Client for Flare.Api's Indexing page endpoint. Field names/casing are a hand-mirror of
// src/Flare.Api/Model/IndexingModels.cs + Json/IndexingJsonContext.cs (camelCase
// properties, no string enums in this response). Keep in sync with those files by hand.

import { API_BASE_URL, apiFetch } from './api';

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

export async function getIndexingStats(signal?: AbortSignal): Promise<IndexingStatsResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/indexing/stats`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/indexing/stats failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
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

export async function getClusterStatus(signal?: AbortSignal): Promise<ClusterStatusResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/indexing/cluster`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/indexing/cluster failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
