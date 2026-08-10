// Client for Flare.Api's Indexing page endpoint. Field names/casing are a hand-mirror of
// src/Flare.Api/Model/IndexingModels.cs + Json/IndexingJsonContext.cs (camelCase
// properties, no string enums in this response). Keep in sync with those files by hand.

import { API_BASE_URL } from './api';

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

export interface IndexingStatsResponse {
	generatedAt: string;
	tables: TableStorageInfo[];
	skipIndexes: SkipIndexInfo[];
	growth: StorageGrowthPoint[];
	growthAvailable: boolean;
}

export async function getIndexingStats(signal?: AbortSignal): Promise<IndexingStatsResponse> {
	const res = await fetch(`${API_BASE_URL}/api/indexing/stats`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/indexing/stats failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
