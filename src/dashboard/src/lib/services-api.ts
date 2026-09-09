// Client for Flare.Api's Services-tab endpoint (per-service RED metrics overview,
// surfaced as a tab on the Traces page rather than its own route).
//
// MemoryPack over the wire, same shape as `indexing-api.ts`'s header comment: `ServiceMetrics`
// has no DateTimeOffset/JsonElement/IReadOnlyList member and is a real generated class;
// `ServiceOverviewResponse` (an IReadOnlyList-of-object member) is hand-written
// (`$lib/memorypack/ServiceOverviewResponse.ts`).

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders } from './api';
import { ServiceOverviewResponse as GeneratedServiceOverviewResponse } from '$lib/memorypack/ServiceOverviewResponse';
import type { ServiceMetrics as GeneratedServiceMetrics } from '$lib/generated/memorypack/ServiceMetrics.js';

export interface ServiceMetrics {
	serviceName: string;
	requestCount: number;
	errorCount: number;
	errorRate: number;
	requestsPerSecond: number;
	p50DurationMs: number;
	p95DurationMs: number;
	p99DurationMs: number;
}

export interface ServiceOverviewResponse {
	windowMinutes: number;
	services: ServiceMetrics[];
}

function toServiceMetrics(dto: GeneratedServiceMetrics): ServiceMetrics {
	return {
		serviceName: dto.serviceName ?? '',
		requestCount: Number(dto.requestCount),
		errorCount: Number(dto.errorCount),
		errorRate: dto.errorRate,
		requestsPerSecond: dto.requestsPerSecond,
		p50DurationMs: dto.p50DurationMs,
		p95DurationMs: dto.p95DurationMs,
		p99DurationMs: dto.p99DurationMs
	};
}

/** @param windowMinutes Lookback window, minutes. Server clamps/defaults - see `ServiceOverviewQueryBuilder.ClampWindowMinutes`. */
export async function getServiceOverview(windowMinutes: number, signal?: AbortSignal): Promise<ServiceOverviewResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/services/overview?windowMinutes=${windowMinutes}`, {
		headers: memoryPackAcceptHeaders(),
		signal
	});
	if (!res.ok) {
		throw new Error(`GET /api/services/overview failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedServiceOverviewResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding ServiceOverviewResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		services: (dto.services ?? []).map((s) => toServiceMetrics(s!))
	};
}
