// Client for Flare.Api's /external-apis page endpoints (src/Flare.Api/Endpoints/
// ExternalApiEndpoints.cs): the domain list (`POST /api/external-apis/domains`) and one
// domain's drill-down (`POST /api/external-apis/domain-detail`), derived from client spans'
// `server.address`/`url.full`/`http.*` attributes - see ExternalApiQueryBuilder.cs and
// docs-internal/adr/0071-external-api-monitoring.md.
//
// MemoryPack over the wire, same shape as `messaging-api.ts`: both requests and every row
// type are generated classes; the two responses are hand-written under `$lib/memorypack/`.

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { ExternalDomainsRequest as GeneratedDomainsRequest } from '$lib/generated/memorypack/ExternalDomainsRequest.js';
import { ExternalDomainDetailRequest as GeneratedDetailRequest } from '$lib/generated/memorypack/ExternalDomainDetailRequest.js';
import { ExternalDomainsResponse as GeneratedDomainsResponse } from '$lib/memorypack/ExternalDomainsResponse';
import { ExternalDomainDetailResponse as GeneratedDetailResponse } from '$lib/memorypack/ExternalDomainDetailResponse';

/** One external domain's outbound calls. Latencies are milliseconds. */
export interface ExternalDomain {
	domain: string;
	callCount: number;
	errorCount: number;
	perSecond: number;
	p50Ms: number;
	p95Ms: number;
	p99Ms: number;
	serviceCount: number;
	endpointCount: number;
	lastSeenUnixMs: number;
	/** The ports called, numeric order, comma-joined (`"443, 8443"`); '' when none was known. */
	ports: string;
}

export interface ExternalDomainsResponse {
	windowMinutes: number;
	domains: ExternalDomain[];
	/** Every calling service in the window - the toolbar picker, unaffected by the service filter. */
	services: string[];
}

/**
 * Which rule named an endpoint - `ExternalEndpointSource` (ExternalApiModels.cs), by ordinal.
 * `UrlTemplate`: the instrumentation's `url.template`; `UrlPath`: the URL's path with id-like
 * segments replaced by `{id}`; `Rpc`: `rpc.service/rpc.method`; `SpanName`: the span name.
 */
export type ExternalEndpointSource = 'UrlTemplate' | 'UrlPath' | 'Rpc' | 'SpanName';

const ENDPOINT_SOURCES: ExternalEndpointSource[] = ['UrlTemplate', 'UrlPath', 'Rpc', 'SpanName'];

function endpointSource(ordinal: number): ExternalEndpointSource {
	return ENDPOINT_SOURCES[ordinal] ?? 'SpanName';
}

export interface ExternalEndpoint {
	/** Empty for non-HTTP calls. */
	method: string;
	endpoint: string;
	endpointSource: ExternalEndpointSource;
	callCount: number;
	errorCount: number;
	perSecond: number;
	p50Ms: number;
	p95Ms: number;
	p99Ms: number;
	lastSeenUnixMs: number;
}

export interface ExternalStatusCodeCount {
	statusCode: string;
	callCount: number;
}

export interface ExternalCaller {
	serviceName: string;
	callCount: number;
	errorCount: number;
	perSecond: number;
	p50Ms: number;
	p95Ms: number;
	p99Ms: number;
}

export interface ExternalErrorGroup {
	method: string;
	endpoint: string;
	endpointSource: ExternalEndpointSource;
	/** Empty when the call never got a response. */
	statusCode: string;
	/** `error.type` - an exception type, or the status code again. */
	errorType: string;
	callCount: number;
	lastSeenUnixMs: number;
	sampleMessage: string;
}

/** One time bucket of one domain's calls. Buckets with no calls are absent. */
export interface ExternalSeriesPoint {
	bucketStartUnixMs: number;
	callCount: number;
	errorCount: number;
	p95Ms: number;
}

export interface ExternalDomainDetailResponse {
	domain: string;
	windowMinutes: number;
	endpoints: ExternalEndpoint[];
	statusCodes: ExternalStatusCodeCount[];
	callers: ExternalCaller[];
	topErrors: ExternalErrorGroup[];
	bucketWidthSeconds: number;
	/** Oldest first. */
	series: ExternalSeriesPoint[];
}

export async function getExternalDomains(windowMinutes: number, service: string, signal?: AbortSignal): Promise<ExternalDomainsResponse> {
	const request = new GeneratedDomainsRequest();
	request.windowMinutes = windowMinutes;
	request.service = service || null;

	const res = await apiFetch(`${API_BASE_URL}/api/external-apis/domains`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedDomainsRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/external-apis/domains failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedDomainsResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding ExternalDomainsResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		services: (dto.services ?? []).filter((s): s is string => s != null),
		domains: (dto.domains ?? [])
			.filter((d) => d != null)
			.map((d) => ({
				domain: d.domain ?? '',
				callCount: Number(d.callCount),
				errorCount: Number(d.errorCount),
				perSecond: d.perSecond,
				p50Ms: d.p50Ms,
				p95Ms: d.p95Ms,
				p99Ms: d.p99Ms,
				serviceCount: Number(d.serviceCount),
				endpointCount: Number(d.endpointCount),
				lastSeenUnixMs: Number(d.lastSeenUnixMs),
				ports: d.ports ?? ''
			}))
	};
}

export async function getExternalDomainDetail(
	domain: string,
	windowMinutes: number,
	service: string,
	signal?: AbortSignal
): Promise<ExternalDomainDetailResponse> {
	const request = new GeneratedDetailRequest();
	request.domain = domain;
	request.windowMinutes = windowMinutes;
	request.service = service || null;

	const res = await apiFetch(`${API_BASE_URL}/api/external-apis/domain-detail`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedDetailRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/external-apis/domain-detail failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedDetailResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding ExternalDomainDetailResponse.');
	}
	return {
		domain: dto.domain ?? domain,
		windowMinutes: dto.windowMinutes,
		endpoints: (dto.endpoints ?? [])
			.filter((e) => e != null)
			.map((e) => ({
				method: e.method ?? '',
				endpoint: e.endpoint ?? '',
				endpointSource: endpointSource(e.endpointSource),
				callCount: Number(e.callCount),
				errorCount: Number(e.errorCount),
				perSecond: e.perSecond,
				p50Ms: e.p50Ms,
				p95Ms: e.p95Ms,
				p99Ms: e.p99Ms,
				lastSeenUnixMs: Number(e.lastSeenUnixMs)
			})),
		statusCodes: (dto.statusCodes ?? [])
			.filter((s) => s != null)
			.map((s) => ({ statusCode: s.statusCode ?? '', callCount: Number(s.callCount) })),
		callers: (dto.callers ?? [])
			.filter((c) => c != null)
			.map((c) => ({
				serviceName: c.serviceName ?? '',
				callCount: Number(c.callCount),
				errorCount: Number(c.errorCount),
				perSecond: c.perSecond,
				p50Ms: c.p50Ms,
				p95Ms: c.p95Ms,
				p99Ms: c.p99Ms
			})),
		topErrors: (dto.topErrors ?? [])
			.filter((e) => e != null)
			.map((e) => ({
				method: e.method ?? '',
				endpoint: e.endpoint ?? '',
				endpointSource: endpointSource(e.endpointSource),
				statusCode: e.statusCode ?? '',
				errorType: e.errorType ?? '',
				callCount: Number(e.callCount),
				lastSeenUnixMs: Number(e.lastSeenUnixMs),
				sampleMessage: e.sampleMessage ?? ''
			})),
		bucketWidthSeconds: dto.bucketWidthSeconds,
		series: (dto.series ?? [])
			.filter((p) => p != null)
			.map((p) => ({
				bucketStartUnixMs: Number(p.bucketStartUnixMs),
				callCount: Number(p.callCount),
				errorCount: Number(p.errorCount),
				p95Ms: p.p95Ms
			}))
	};
}
