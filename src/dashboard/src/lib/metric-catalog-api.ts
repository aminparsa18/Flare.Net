// Client for Flare.Api's Metrics catalog endpoints (src/Flare.Api/Endpoints/
// MetricCatalogEndpoints.cs): every ingested metric with its cardinality
// (`POST /api/metrics/catalog`), one metric's drill-down (`POST /api/metrics/catalog/detail`),
// its inspect view (`POST /api/metrics/catalog/inspect`) - see MetricCatalogQueryBuilder.cs /
// MetricInspectReducer.cs - and the Admin-only unit/description overrides
// (`PUT`/`DELETE /api/metrics/metadata-overrides`, ADR-0065) with "treat as counter" (ADR-0066).
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
import { MetricCatalogInspectRequest as GeneratedMetricCatalogInspectRequest } from '$lib/generated/memorypack/MetricCatalogInspectRequest.js';
import { MetricCatalogInspectResponse as GeneratedMetricCatalogInspectResponse } from '$lib/memorypack/MetricCatalogInspectResponse';
import { SetMetricMetadataOverrideRequest as GeneratedSetMetricMetadataOverrideRequest } from '$lib/generated/memorypack/SetMetricMetadataOverrideRequest.js';

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
	/** Unit/description come (at least partly) from an admin override. */
	hasMetadataOverride: boolean;
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
	/** What the instrumentation sent, before any override. */
	emittedUnit: string | null;
	emittedDescription: string | null;
	hasMetadataOverride: boolean;
	/** Admin "treat as counter" setting (ADR-0066) - only meaningful for a Gauge. */
	treatAsCounter: boolean;
}

/** How a raw sample feeds its bucket - mirrors MetricSeriesQueryBuilder's Sum classification (ADR-0035). */
export type MetricInspectSampleKind = 'Level' | 'Delta' | 'First' | 'Difference' | 'Reset';
const SAMPLE_KINDS: MetricInspectSampleKind[] = ['Level', 'Delta', 'First', 'Difference', 'Reset'];

export interface MetricInspectSample {
	timeMs: number;
	/** For a histogram: the point's observation count. */
	value: number;
	kind: MetricInspectSampleKind;
	/** What the sample adds to its bucket. */
	contribution: number;
}

export interface MetricInspectBucket {
	bucketStartMs: number;
	value: number;
	sampleCount: number;
}

export interface MetricInspectSeries {
	serviceName: string;
	attributes: Record<string, string>;
	/** Oldest first. */
	samples: MetricInspectSample[];
	/** The series had more samples than returned; the oldest were dropped. */
	samplesTruncated: boolean;
	/** Step 1 - time aggregation. */
	buckets: MetricInspectBucket[];
}

export interface MetricInspectResult {
	metricName: string;
	type: MetricPointType;
	windowMinutes: number;
	bucketWidthSeconds: number;
	totalSeriesCount: number;
	series: MetricInspectSeries[];
	/** Step 2 - space aggregation across `series`. */
	merged: MetricInspectBucket[];
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
				lastReceivedMs: Number(e.lastReceivedUnixMs),
				hasMetadataOverride: e.hasMetadataOverride
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
			})),
		emittedUnit: dto.emittedUnit || null,
		emittedDescription: dto.emittedDescription || null,
		hasMetadataOverride: dto.hasMetadataOverride,
		treatAsCounter: dto.treatAsCounter
	};
}

export interface MetricInspectOptions {
	windowMinutes: number;
	bucketWidthSeconds: number;
	/** Null = every service. */
	serviceName: string | null;
}

export async function inspectMetric(
	metricName: string,
	type: MetricPointType,
	options: MetricInspectOptions,
	signal?: AbortSignal
): Promise<MetricInspectResult> {
	const request = new GeneratedMetricCatalogInspectRequest();
	request.metricName = metricName;
	request.type = metricPointTypeFromString(type);
	request.windowMinutes = options.windowMinutes;
	request.bucketWidthSeconds = options.bucketWidthSeconds;
	request.serviceName = options.serviceName;

	const res = await apiFetch(`${API_BASE_URL}/api/metrics/catalog/inspect`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedMetricCatalogInspectRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/metrics/catalog/inspect failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedMetricCatalogInspectResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding MetricCatalogInspectResponse.');
	}
	const bucket = (b: { bucketStartUnixMs: bigint; value: number; sampleCount: number }): MetricInspectBucket => ({
		bucketStartMs: Number(b.bucketStartUnixMs),
		value: b.value,
		sampleCount: b.sampleCount
	});
	return {
		metricName: dto.metricName ?? '',
		type: metricPointTypeToString(dto.type),
		windowMinutes: dto.windowMinutes,
		bucketWidthSeconds: dto.bucketWidthSeconds,
		totalSeriesCount: Number(dto.totalSeriesCount),
		series: (dto.series ?? [])
			.filter((s) => s != null)
			.map((s) => ({
				serviceName: s.serviceName ?? '',
				attributes: s.attributes ?? {},
				samples: (s.samples ?? [])
					.filter((x) => x != null)
					.map((x) => ({
						timeMs: Number(x.timeUnixMs),
						value: x.value,
						kind: SAMPLE_KINDS[x.kind] ?? 'Level',
						contribution: x.contribution
					})),
				samplesTruncated: s.samplesTruncated,
				buckets: (s.buckets ?? []).filter((b) => b != null).map(bucket)
			})),
		merged: (dto.merged ?? []).filter((b) => b != null).map(bucket)
	};
}

/**
 * `PUT /api/metrics/metadata-overrides` - Admin-only. A null member shows the emitted value;
 * `treatAsCounter` charts a Gauge like a counter (ADR-0066).
 */
export async function setMetricMetadataOverride(
	metricName: string,
	unit: string | null,
	description: string | null,
	treatAsCounter: boolean
): Promise<void> {
	const request = new GeneratedSetMetricMetadataOverrideRequest();
	request.metricName = metricName;
	request.unit = unit;
	request.description = description;
	request.treatAsCounter = treatAsCounter;

	const res = await apiFetch(`${API_BASE_URL}/api/metrics/metadata-overrides`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSetMetricMetadataOverrideRequest.serialize(request))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/metrics/metadata-overrides failed: ${res.status} ${res.statusText}`);
	}
}

/** `DELETE /api/metrics/metadata-overrides` - Admin-only. Reverts the metric to its emitted unit/description and clears "treat as counter". */
export async function resetMetricMetadataOverride(metricName: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/metrics/metadata-overrides?metricName=${encodeURIComponent(metricName)}`, {
		method: 'DELETE'
	});
	if (!res.ok) {
		throw new Error(`DELETE /api/metrics/metadata-overrides failed: ${res.status} ${res.statusText}`);
	}
}

export interface MetricDashboardPanelUsage {
	panelId: string;
	title: string;
	/** The metric is one of a Formula panel's queries rather than the panel's single selected metric. */
	inFormula: boolean;
}

export interface MetricDashboardUsage {
	dashboardId: string;
	dashboardName: string;
	panels: MetricDashboardPanelUsage[];
}

/** Dashboards with a Metrics panel reading `metricName`, by dashboard name. Plain JSON - a small nested list isn't worth a MemoryPack companion. */
export async function getMetricDashboardUsage(metricName: string, signal?: AbortSignal): Promise<MetricDashboardUsage[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/metrics/catalog/dashboards`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ metricName }),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/metrics/catalog/dashboards failed: ${res.status} ${res.statusText}`);
	}
	return ((await res.json()) as { dashboards: MetricDashboardUsage[] }).dashboards;
}
