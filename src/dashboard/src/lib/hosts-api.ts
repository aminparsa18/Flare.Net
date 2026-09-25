// Client for Flare.Api's Hosts page endpoints (src/Flare.Api/Endpoints/
// HostInventoryEndpoints.cs): the host inventory table (`POST /api/hosts`) and one host's
// drill-down series (`POST /api/hosts/metrics`), both derived from ingested OTel
// `hostmetrics`-receiver metrics - see HostInventoryQueryBuilder.cs for which metric feeds
// which column.
//
// MemoryPack over the wire, same shape as `services-api.ts`: both request types have no
// DateTimeOffset/list member and are real generated classes; every response carries a
// DateTimeOffset (`HostSummary.LastSeen`/`HostMetricsPoint.BucketStart`) inside an
// IReadOnlyList, so those are hand-written under `$lib/memorypack/`.

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { HostListRequest as GeneratedHostListRequest } from '$lib/generated/memorypack/HostListRequest.js';
import { HostMetricsRequest as GeneratedHostMetricsRequest } from '$lib/generated/memorypack/HostMetricsRequest.js';
import { HostListResponse as GeneratedHostListResponse } from '$lib/memorypack/HostListResponse';
import { HostMetricsResponse as GeneratedHostMetricsResponse } from '$lib/memorypack/HostMetricsResponse';

/** Utilization figures are percentages (0-100) averaged over the window; null = the host sent no data for that metric (e.g. the filesystem scraper is off), never 0. */
export interface HostSummary {
	hostName: string;
	osType: string | null;
	cpuPercent: number | null;
	memoryPercent: number | null;
	diskPercent: number | null;
	/** `system.cpu.load_average.15m` - a raw load figure, not a percentage. */
	loadAverage15m: number | null;
	lastSeen: string;
}

export interface HostListResponse {
	windowMinutes: number;
	hosts: HostSummary[];
	/** More hosts matched than the server's cap - narrow the filter. */
	truncated: boolean;
}

export interface HostMetricsPoint {
	bucketStart: string;
	cpuPercent: number | null;
	memoryPercent: number | null;
	diskPercent: number | null;
	loadAverage15m: number | null;
}

export interface HostMetricsResponse {
	hostName: string;
	windowMinutes: number;
	bucketWidthSeconds: number;
	points: HostMetricsPoint[];
}

export interface HostListFilter {
	search?: string;
	osType?: string;
}

export async function listHosts(windowMinutes: number, filter: HostListFilter, signal?: AbortSignal): Promise<HostListResponse> {
	const request = new GeneratedHostListRequest();
	request.windowMinutes = windowMinutes;
	request.search = filter.search?.trim() || null;
	request.osType = filter.osType || null;

	const res = await apiFetch(`${API_BASE_URL}/api/hosts`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedHostListRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/hosts failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedHostListResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding HostListResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		truncated: dto.truncated,
		hosts: (dto.hosts ?? [])
			.filter((h) => h != null)
			.map((h) => ({
				hostName: h.hostName,
				osType: h.osType,
				cpuPercent: h.cpuPercent,
				memoryPercent: h.memoryPercent,
				diskPercent: h.diskPercent,
				loadAverage15m: h.loadAverage15m,
				lastSeen: h.lastSeen.toISOString()
			}))
	};
}

/** `endMs` (epoch ms) anchors the window's end - omitted = now. The log event detail view passes one to chart a window around a past log's timestamp. */
export async function getHostMetrics(hostName: string, windowMinutes: number, signal?: AbortSignal, endMs?: number): Promise<HostMetricsResponse> {
	const request = new GeneratedHostMetricsRequest();
	request.hostName = hostName;
	request.windowMinutes = windowMinutes;
	request.endUnixMs = endMs == null ? null : BigInt(Math.round(endMs));

	const res = await apiFetch(`${API_BASE_URL}/api/hosts/metrics`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedHostMetricsRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/hosts/metrics failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedHostMetricsResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding HostMetricsResponse.');
	}
	return {
		hostName: dto.hostName,
		windowMinutes: dto.windowMinutes,
		bucketWidthSeconds: dto.bucketWidthSeconds,
		points: (dto.points ?? [])
			.filter((p) => p != null)
			.map((p) => ({
				bucketStart: p.bucketStart.toISOString(),
				cpuPercent: p.cpuPercent,
				memoryPercent: p.memoryPercent,
				diskPercent: p.diskPercent,
				loadAverage15m: p.loadAverage15m
			}))
	};
}
