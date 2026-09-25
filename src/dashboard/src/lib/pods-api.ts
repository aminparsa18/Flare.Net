// Client for Flare.Api's `POST /api/pods/metrics` (src/Flare.Api/Endpoints/PodMetricsEndpoints.cs):
// one Kubernetes pod's CPU/memory per time bucket, from the OTel `kubeletstats` receiver -
// see PodMetricsQueryBuilder.cs for which metric feeds which field.
//
// MemoryPack over the wire, same shape as `hosts-api.ts`: the request has no
// DateTimeOffset/list member and is a real generated class; the response carries
// `PodMetricsPoint.BucketStart` inside an IReadOnlyList, so it's hand-written under
// `$lib/memorypack/`.

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { PodMetricsRequest as GeneratedPodMetricsRequest } from '$lib/generated/memorypack/PodMetricsRequest.js';
import { PodMetricsResponse as GeneratedPodMetricsResponse } from '$lib/memorypack/PodMetricsResponse';

/** Every field is a bucket average; null = the pod sent no data for it (the two limit figures are opt-in kubeletstats metrics, so usually null). */
export interface PodMetricsPoint {
	bucketStart: string;
	cpuCores: number | null;
	memoryWorkingSetBytes: number | null;
	cpuLimitPercent: number | null;
	memoryLimitPercent: number | null;
}

export interface PodMetricsResponse {
	podName: string;
	windowMinutes: number;
	bucketWidthSeconds: number;
	points: PodMetricsPoint[];
}

export interface PodMetricsQuery {
	podName: string;
	/** `k8s.namespace.name`; omitted = the pod name in any namespace. */
	namespace?: string;
	windowMinutes: number;
	/** Epoch ms the window ends at; omitted = now. */
	endMs?: number;
}

export async function getPodMetrics(query: PodMetricsQuery, signal?: AbortSignal): Promise<PodMetricsResponse> {
	const request = new GeneratedPodMetricsRequest();
	request.podName = query.podName;
	request.namespace = query.namespace || null;
	request.windowMinutes = query.windowMinutes;
	request.endUnixMs = query.endMs == null ? null : BigInt(Math.round(query.endMs));

	const res = await apiFetch(`${API_BASE_URL}/api/pods/metrics`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedPodMetricsRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/pods/metrics failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedPodMetricsResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding PodMetricsResponse.');
	}
	return {
		podName: dto.podName,
		windowMinutes: dto.windowMinutes,
		bucketWidthSeconds: dto.bucketWidthSeconds,
		points: (dto.points ?? [])
			.filter((p) => p != null)
			.map((p) => ({
				bucketStart: p.bucketStart.toISOString(),
				cpuCores: p.cpuCores,
				memoryWorkingSetBytes: p.memoryWorkingSetBytes,
				cpuLimitPercent: p.cpuLimitPercent,
				memoryLimitPercent: p.memoryLimitPercent
			}))
	};
}
