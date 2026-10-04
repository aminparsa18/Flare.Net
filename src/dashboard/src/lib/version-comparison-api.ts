// Client for Flare.Api's deploy / version comparison (`POST /api/services/version-comparison`, see
// src/Flare.Api/Endpoints/ServicesEndpoints.cs and VersionComparisonQueryBuilder.cs). Plain JSON, like
// `runtime-health-api.ts` - small flat lists, so no MemoryPack adapter to maintain.

import { API_BASE_URL, apiFetch } from './api';

export interface ServiceVersionInfo {
	version: string;
	/** Earliest span carrying this version within the lookback (epoch ms) - so clamped to it. */
	firstSeenUnixMs: number;
	lastSeenUnixMs: number;
	spanCount: number;
}

export interface VersionEndpointStats {
	count: number;
	errorCount: number;
	p95DurationMs: number;
}

export interface VersionEndpointComparison {
	endpoint: string;
	/** Null when that version never served this endpoint. */
	baseline: VersionEndpointStats | null;
	current: VersionEndpointStats | null;
}

export interface VersionNewException {
	exceptionType: string;
	count: number;
	firstSeenUnixMs: number;
}

export interface VersionNewDependency {
	kind: 'External' | 'Database';
	target: string;
	callCount: number;
	errorCount: number;
}

export interface VersionNewLogPattern {
	patternId: string;
	template: string;
	count: number;
	maxSeverityNumber: number;
	firstSeenUnixMs: number;
}

export interface VersionComparisonResponse {
	lookbackHours: number;
	/** Newest first-seen first. */
	versions: ServiceVersionInfo[];
	/** The resolved pair; baseline is null when there is nothing to compare against. */
	baselineVersion: string | null;
	currentVersion: string | null;
	endpoints: VersionEndpointComparison[];
	newExceptions: VersionNewException[];
	newDependencies: VersionNewDependency[];
	newLogPatterns: VersionNewLogPattern[];
}

export async function getVersionComparison(
	service: string,
	baselineVersion: string | null,
	currentVersion: string | null,
	lookbackHours: number,
	signal?: AbortSignal
): Promise<VersionComparisonResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/services/version-comparison`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ service, baselineVersion, currentVersion, lookbackHours }),
		signal
	});
	if (!res.ok) throw new Error(`Version comparison query failed: ${res.status} ${res.statusText}`);
	return (await res.json()) as VersionComparisonResponse;
}
