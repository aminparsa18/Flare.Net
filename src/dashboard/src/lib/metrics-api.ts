// Client for Flare.Api's metrics Query API (metric discovery, time-bucketed series
// query).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape.
// `MetricAttributeFilter`/`MetricNameInfo`/`MetricNamesResponse`/`MetricAttributeKeyInfo`/
// `MetricAttributeKeysResponse` have no DateTimeOffset/JsonElement/IReadOnlyList-of-object
// member and use real generated classes; everything nesting `MetricFilter` (`DateTimeOffset?`
// members) or `MetricSeriesPoint` (`DateTimeOffset`) is hand-written (`$lib/memorypack/`).
// `type` converts through `$lib/memorypack/enums.ts`'s `metricPointTypeToString`/`FromString`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import { metricPointTypeFromString, metricPointTypeToString, type MetricPointTypeName } from '$lib/memorypack/enums';
import { MetricFilter as GeneratedMetricFilter } from '$lib/memorypack/MetricFilter';
import { MetricNamesRequest as GeneratedMetricNamesRequest } from '$lib/memorypack/MetricNamesRequest';
import { MetricNamesResponse as GeneratedMetricNamesResponse } from '$lib/memorypack/MetricNamesResponse';
import { MetricAttributeKeysRequest as GeneratedMetricAttributeKeysRequest } from '$lib/memorypack/MetricAttributeKeysRequest';
import { MetricAttributeKeysResponse as GeneratedMetricAttributeKeysResponse } from '$lib/memorypack/MetricAttributeKeysResponse';
import { MetricQueryRequest as GeneratedMetricQueryRequest } from '$lib/memorypack/MetricQueryRequest';
import { MetricQueryResponse as GeneratedMetricQueryResponse } from '$lib/memorypack/MetricQueryResponse';
import { MetricAttributeFilter as GeneratedMetricAttributeFilter } from '$lib/generated/memorypack/MetricAttributeFilter.js';
import type { MetricNameInfo as GeneratedMetricNameInfo } from '$lib/generated/memorypack/MetricNameInfo.js';
import type { MetricAttributeKeyInfo as GeneratedMetricAttributeKeyInfo } from '$lib/generated/memorypack/MetricAttributeKeyInfo.js';
import type { MetricSeries as GeneratedMetricSeries } from '$lib/memorypack/MetricSeries';
import type { MetricSeriesPoint as GeneratedMetricSeriesPoint } from '$lib/memorypack/MetricSeriesPoint';

// ---- Shared filter shape (MetricFilter.cs) ---------------------------------

export interface MetricAttributeFilter {
	key: string;
	value: string;
}

export interface MetricFilter {
	from?: string;
	to?: string;
	services?: string[];
	attributes?: MetricAttributeFilter[];
}

export type MetricPointType = MetricPointTypeName;

function toGeneratedMetricFilter(filter: MetricFilter | undefined): GeneratedMetricFilter {
	const dto = new GeneratedMetricFilter();
	if (filter == null) return dto;
	dto.from = filter.from == null ? null : new Date(filter.from);
	dto.to = filter.to == null ? null : new Date(filter.to);
	dto.services = filter.services ?? null;
	dto.attributes =
		filter.attributes == null
			? null
			: filter.attributes.map((a) => {
					const attr = new GeneratedMetricAttributeFilter();
					attr.key = a.key;
					attr.value = a.value;
					return attr;
				});
	return dto;
}

// ---- POST /api/metrics/names (MetricNamesRequest.cs / MetricNamesResponse) -

export interface MetricNamesRequest {
	from?: string;
	to?: string;
	services?: string[];
}

export interface MetricNameInfo {
	metricName: string;
	serviceName: string;
	type: MetricPointType;
	unit: string | null;
	description: string | null;
	// How many distinct chart lines selecting this metric will produce - see
	// MetricNamesQueryBuilder's remarks. Lets the picker show it up front, before
	// the metric is ever selected/queried.
	seriesCount: number;
}

export interface MetricNamesResponse {
	metrics: MetricNameInfo[];
}

function toMetricNameInfo(dto: GeneratedMetricNameInfo): MetricNameInfo {
	return {
		metricName: dto.metricName ?? '',
		serviceName: dto.serviceName ?? '',
		type: metricPointTypeToString(dto.type),
		unit: dto.unit,
		description: dto.description,
		seriesCount: Number(dto.seriesCount)
	};
}

export async function getMetricNames(request: MetricNamesRequest = {}, signal?: AbortSignal): Promise<MetricNamesResponse> {
	const dto = new GeneratedMetricNamesRequest();
	dto.from = request.from == null ? null : new Date(request.from);
	dto.to = request.to == null ? null : new Date(request.to);
	dto.services = request.services ?? null;
	const res = await apiFetch(`${API_BASE_URL}/api/metrics/names`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedMetricNamesRequest.serialize(dto)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/metrics/names failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedMetricNamesResponse.deserialize(await res.arrayBuffer());
	return { metrics: (body?.metrics ?? []).map((m) => toMetricNameInfo(m!)) };
}

// ---- POST /api/metrics/attribute-keys (MetricAttributeKeysRequest.cs / MetricAttributeKeysResponse.cs) -

export interface MetricAttributeKeysRequest {
	metricName: string;
	type: MetricPointType;
	filter?: MetricFilter;
}

export interface MetricAttributeKeyInfo {
	key: string;
	// Distinct-value count for this key in scope - same "surface cardinality before
	// selection" reasoning as MetricNameInfo.seriesCount, so the "Group by" picker can
	// show e.g. "error.type (3)" vs "host.name (47)".
	distinctValueCount: number;
}

export interface MetricAttributeKeysResponse {
	keys: MetricAttributeKeyInfo[];
}

function toMetricAttributeKeyInfo(dto: GeneratedMetricAttributeKeyInfo): MetricAttributeKeyInfo {
	return { key: dto.key ?? '', distinctValueCount: Number(dto.distinctValueCount) };
}

export async function getMetricAttributeKeys(
	request: MetricAttributeKeysRequest,
	signal?: AbortSignal
): Promise<MetricAttributeKeysResponse> {
	const dto = new GeneratedMetricAttributeKeysRequest();
	dto.metricName = request.metricName;
	dto.type = metricPointTypeFromString(request.type);
	dto.filter = toGeneratedMetricFilter(request.filter);
	const res = await apiFetch(`${API_BASE_URL}/api/metrics/attribute-keys`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedMetricAttributeKeysRequest.serialize(dto)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/metrics/attribute-keys failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedMetricAttributeKeysResponse.deserialize(await res.arrayBuffer());
	return { keys: (body?.keys ?? []).map((k) => toMetricAttributeKeyInfo(k!)) };
}

// ---- POST /api/metrics/query (MetricQueryRequest.cs / MetricQueryResponse) -

export interface MetricQueryRequest {
	metricName: string;
	type: MetricPointType;
	filter?: MetricFilter;
	bucketWidthSeconds: number;
	// Attribute key that defines series identity when set, collapsing every series
	// sharing that one key's value - see MetricSeriesQueryBuilder's remarks. Omitted/
	// undefined = ungrouped (one series per distinct attribute map).
	groupByAttributeKey?: string;
}

export interface MetricSeriesPoint {
	bucketStart: string;
	/** Gauge/Sum only. */
	value: number | null;
	/** Sum: raw sample count. Histogram: total observation count. */
	count: number | null;
	/** Histogram only. */
	sum: number | null;
	p50: number | null;
	p75: number | null;
	p90: number | null;
	p95: number | null;
	p99: number | null;
	/** Histogram only. Approximate - upper bound of the highest non-empty bucket, not the true OTLP max. */
	maxApprox: number | null;
}

export interface MetricSeries {
	serviceName: string;
	// Full DataPointAttributes map when the request was ungrouped; a single-entry map
	// ({ [groupByAttributeKey]: value }) when MetricQueryRequest.groupByAttributeKey was
	// set - see MetricSeries.cs' remarks.
	attributes: Record<string, string>;
	points: MetricSeriesPoint[];
}

export interface MetricQueryResponse {
	series: MetricSeries[];
}

function toMetricSeriesPoint(dto: GeneratedMetricSeriesPoint): MetricSeriesPoint {
	return {
		bucketStart: dto.bucketStart.toISOString(),
		value: dto.value,
		count: dto.count == null ? null : Number(dto.count),
		sum: dto.sum,
		p50: dto.p50,
		p75: dto.p75,
		p90: dto.p90,
		p95: dto.p95,
		p99: dto.p99,
		maxApprox: dto.maxApprox
	};
}

function toMetricSeries(dto: GeneratedMetricSeries): MetricSeries {
	return {
		serviceName: dto.serviceName ?? '',
		attributes: dto.attributes ?? {},
		points: (dto.points ?? []).map((p) => toMetricSeriesPoint(p!))
	};
}

export async function queryMetric(request: MetricQueryRequest, signal?: AbortSignal): Promise<MetricQueryResponse> {
	const dto = new GeneratedMetricQueryRequest();
	dto.metricName = request.metricName;
	dto.type = metricPointTypeFromString(request.type);
	dto.filter = toGeneratedMetricFilter(request.filter);
	dto.bucketWidthSeconds = request.bucketWidthSeconds;
	dto.groupByAttributeKey = request.groupByAttributeKey ?? null;
	const res = await apiFetch(`${API_BASE_URL}/api/metrics/query`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedMetricQueryRequest.serialize(dto)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/metrics/query failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedMetricQueryResponse.deserialize(await res.arrayBuffer());
	return { series: (body?.series ?? []).map((s) => toMetricSeries(s!)) };
}
