// Client for Flare.Api's Metrics catalog endpoints (src/Flare.Api/Endpoints/
// MetricCatalogEndpoints.cs): every ingested metric with its cardinality
// (`POST /api/metrics/catalog`) and one metric's drill-down (`POST /api/metrics/catalog/detail`)
// - see MetricCatalogQueryBuilder.cs for the SQL.
//
// MemoryPack over the wire, same shape as `hosts-api.ts`: both requests and the per-row
// entry/service/related types are real generated classes (timestamps travel as epoch ms to
// keep them generatable); the responses and `MetricCatalogAttributeInfo` carry an
// IReadOnlyList, so those are hand-written under `$lib/memorypack/`.

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { metricPointTypeFromString, metricPointTypeToString } from '$lib/memorypack/enums';
import type { MetricPointType } from './metrics-api';
import { MetricCatalogRequest as GeneratedMetricCatalogRequest } from '$lib/generated/memorypack/MetricCatalogRequest.js';
import { MetricCatalogDetailRequest as GeneratedMetricCatalogDetailRequest } from '$lib/generated/memorypack/MetricCatalogDetailRequest.js';
import { MetricCatalogResponse as GeneratedMetricCatalogResponse } from '$lib/memorypack/MetricCatalogResponse';
import { MetricCatalogDetailResponse as GeneratedMetricCatalogDetailResponse } from '$lib/memorypack/MetricCatalogDetailResponse';

export interface MetricCatalogEntry {
	metricName: string;
	type: MetricPointType;
	unit: string | null;
	description: string | null;
	serviceCount: number;
	/** Distinct (service, attribute set) pairs in the window - approximate past a few thousand. */
	seriesCount: number;
	sampleCount: number;
	lastReceivedMs: number;
}

export interface MetricCatalogResponse {
	windowMinutes: number;
	metrics: MetricCatalogEntry[];
	/** More metrics matched than the server's cap - the lowest-cardinality ones were dropped. */
	truncated: boolean;
}

export interface MetricCatalogServiceInfo {
	serviceName: string;
	seriesCount: number;
	sampleCount: number;
	lastReceivedMs: number;
}

export interface MetricCatalogAttributeInfo {
	key: string;
	distinctValueCount: number;
	/** Data points carrying this key - compare against the metric's total to see how often it's set. */
	sampleCount: number;
	/** Most frequent values first. */
	sampleValues: string[];
}

export interface MetricCatalogRelatedMetric {
	metricName: string;
	type: MetricPointType;
	sharedNamePrefix: string | null;
	sharedAttributeKeyCount: number;
	sharedServiceCount: number;
}

export interface MetricCatalogDetail {
	metricName: string;
	type: MetricPointType;
	unit: string | null;
	description: string | null;
	windowMinutes: number;
	services: MetricCatalogServiceInfo[];
	attributes: MetricCatalogAttributeInfo[];
	related: MetricCatalogRelatedMetric[];
}

export async function listMetricCatalog(windowMinutes: number, search: string, signal?: AbortSignal): Promise<MetricCatalogResponse> {
	const request = new GeneratedMetricCatalogRequest();
	request.windowMinutes = windowMinutes;
	request.search = search.trim() || null;

	const res = await apiFetch(`${API_BASE_URL}/api/metrics/catalog`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedMetricCatalogRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/metrics/catalog failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedMetricCatalogResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding MetricCatalogResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		truncated: dto.truncated,
		metrics: (dto.metrics ?? [])
			.filter((e) => e != null)
			.map((e) => ({
				metricName: e.metricName ?? '',
				type: metricPointTypeToString(e.type),
				unit: e.unit || null,
				description: e.description || null,
				serviceCount: Number(e.serviceCount),
				seriesCount: Number(e.seriesCount),
				sampleCount: Number(e.sampleCount),
				lastReceivedMs: Number(e.lastReceivedUnixMs)
			}))
	};
}

export async function getMetricCatalogDetail(
	metricName: string,
	type: MetricPointType,
	windowMinutes: number,
	signal?: AbortSignal
): Promise<MetricCatalogDetail> {
	const request = new GeneratedMetricCatalogDetailRequest();
	request.metricName = metricName;
	request.type = metricPointTypeFromString(type);
	request.windowMinutes = windowMinutes;

	const res = await apiFetch(`${API_BASE_URL}/api/metrics/catalog/detail`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedMetricCatalogDetailRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/metrics/catalog/detail failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedMetricCatalogDetailResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding MetricCatalogDetailResponse.');
	}
	return {
		metricName: dto.metricName,
		type: metricPointTypeToString(dto.type),
		unit: dto.unit || null,
		description: dto.description || null,
		windowMinutes: dto.windowMinutes,
		services: (dto.services ?? [])
			.filter((s) => s != null)
			.map((s) => ({
				serviceName: s.serviceName ?? '',
				seriesCount: Number(s.seriesCount),
				sampleCount: Number(s.sampleCount),
				lastReceivedMs: Number(s.lastReceivedUnixMs)
			})),
		attributes: (dto.attributes ?? [])
			.filter((a) => a != null)
			.map((a) => ({
				key: a.key,
				distinctValueCount: Number(a.distinctValueCount),
				sampleCount: Number(a.sampleCount),
				sampleValues: (a.sampleValues ?? []).filter((v): v is string => v != null)
			})),
		related: (dto.related ?? [])
			.filter((r) => r != null)
			.map((r) => ({
				metricName: r.metricName ?? '',
				type: metricPointTypeToString(r.type),
				sharedNamePrefix: r.sharedNamePrefix || null,
				sharedAttributeKeyCount: r.sharedAttributeKeyCount,
				sharedServiceCount: r.sharedServiceCount
			}))
	};
}
