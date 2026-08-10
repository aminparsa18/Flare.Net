// Client for Flare.Api's metrics Query API (metric discovery, time-bucketed series
// query). Field names/casing/enum values are a hand-mirror of
// src/Flare.Api/Model/MetricModels.cs + Json/MetricsJsonContext.cs - same
// camelCase-properties/PascalCase-string-enum-values convention `api.ts` documents for
// LogsJsonContext (`UseStringEnumConverter` with no naming policy leaves enum member
// names as-is - confirmed against a real response during Pass 2's e2e verification,
// e.g. `"type": "Gauge"`). Keep in sync with those files by hand.

import { API_BASE_URL, apiFetch } from './api';

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

export type MetricPointType = 'Gauge' | 'Sum' | 'Histogram';

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
}

export interface MetricNamesResponse {
	metrics: MetricNameInfo[];
}

export async function getMetricNames(request: MetricNamesRequest = {}, signal?: AbortSignal): Promise<MetricNamesResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/metrics/names`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/metrics/names failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

// ---- POST /api/metrics/query (MetricQueryRequest.cs / MetricQueryResponse) -

export interface MetricQueryRequest {
	metricName: string;
	type: MetricPointType;
	filter?: MetricFilter;
	bucketWidthSeconds: number;
}

export interface MetricSeriesPoint {
	bucketStart: string;
	/** Gauge/Sum only. */
	value: number | null;
	/** Histogram only. */
	count: number | null;
	sum: number | null;
	p50: number | null;
	p90: number | null;
	p99: number | null;
}

export interface MetricSeries {
	serviceName: string;
	attributes: Record<string, string>;
	points: MetricSeriesPoint[];
}

export interface MetricQueryResponse {
	series: MetricSeries[];
}

export async function queryMetric(request: MetricQueryRequest, signal?: AbortSignal): Promise<MetricQueryResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/metrics/query`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/metrics/query failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
