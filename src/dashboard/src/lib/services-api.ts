// Client for Flare.Api's Services-tab endpoints: per-service RED metrics overview (Table
// view) and the aggregate cross-trace dependency graph (Map view) - both surfaced as a tab
// on the Traces page rather than their own route.
//
// MemoryPack over the wire, same shape as `indexing-api.ts`'s header comment: `ServiceMetrics`
// has no DateTimeOffset/JsonElement/IReadOnlyList member and is a real generated class;
// `ServiceOverviewResponse` (an IReadOnlyList-of-object member) is hand-written
// (`$lib/memorypack/ServiceOverviewResponse.ts`), and so are `ServiceDependencyNode` (an
// IReadOnlyList<string> member) and `ServiceDependencyGraphResponse` (IReadOnlyList-of-object
// members) - see their own header comments.
//
// All three endpoints were plain GET-with-`?windowMinutes=` until the Services tab's
// resource-attribute filter chips (docs-internal/planning/roadmap.md's now-removed
// "Resource-attribute filtering on the Traces > Services tab" item) - see
// `ServicesEndpoints.cs`'s own header comment for why that made them POST. Their request
// bodies (`ServiceOverviewRequest`/`ServiceDependencyRequest`/`ServiceCallBreakdownRequest`)
// are hand-written for the same reason as the response types above: an
// `IReadOnlyList<ResourceAttributeFilter>?` member blocks the generator, even though
// `ResourceAttributeFilter` itself is a real generated class (no list/DateTimeOffset
// member of its own).

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { ServiceOverviewRequest as GeneratedServiceOverviewRequest } from '$lib/memorypack/ServiceOverviewRequest';
import { ServiceOverviewResponse as GeneratedServiceOverviewResponse } from '$lib/memorypack/ServiceOverviewResponse';
import { ServiceDependencyRequest as GeneratedServiceDependencyRequest } from '$lib/memorypack/ServiceDependencyRequest';
import { ServiceDependencyGraphResponse as GeneratedServiceDependencyGraphResponse } from '$lib/memorypack/ServiceDependencyGraphResponse';
import { ServiceCallBreakdownRequest as GeneratedServiceCallBreakdownRequest } from '$lib/memorypack/ServiceCallBreakdownRequest';
import { ServiceCallBreakdownResponse as GeneratedServiceCallBreakdownResponse } from '$lib/memorypack/ServiceCallBreakdownResponse';
import { ResourceAttributeFilter as GeneratedResourceAttributeFilter } from '$lib/generated/memorypack/ResourceAttributeFilter.js';
import type { ServiceMetrics as GeneratedServiceMetrics } from '$lib/generated/memorypack/ServiceMetrics.js';
import type { ServiceDependencyNode as GeneratedServiceDependencyNode } from '$lib/memorypack/ServiceDependencyNode';
import type { ServiceDependencyEdge as GeneratedServiceDependencyEdge } from '$lib/generated/memorypack/ServiceDependencyEdge.js';
import type { ExternalCallGroup as GeneratedExternalCallGroup } from '$lib/generated/memorypack/ExternalCallGroup.js';
import type { DatabaseCallGroup as GeneratedDatabaseCallGroup } from '$lib/generated/memorypack/DatabaseCallGroup.js';
import type { ServiceMapNode, ServiceMapEdge } from '$lib/traces/service-map';

/** See `ResourceAttributeFilter` (ResourceAttributeFilter.cs) - the Services tab's filter chips, e.g. `{ key: 'deployment.environment', value: 'production' }`. */
export interface ResourceAttributeFilter {
	key: string;
	value: string;
}

function toGeneratedResourceAttributes(resourceAttributes: ResourceAttributeFilter[] | undefined): (GeneratedResourceAttributeFilter | null)[] | null {
	if (resourceAttributes == null || resourceAttributes.length === 0) return null;
	return resourceAttributes.map((a) => {
		const attr = new GeneratedResourceAttributeFilter();
		attr.key = a.key;
		attr.value = a.value;
		return attr;
	});
}

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

/**
 * @param windowMinutes Lookback window, minutes. Server clamps/defaults - see `ServiceOverviewQueryBuilder.ClampWindowMinutes`.
 * @param resourceAttributes Optional filter chips, ANDed together server-side. Omit/empty = no narrowing.
 */
export async function getServiceOverview(
	windowMinutes: number,
	resourceAttributes?: ResourceAttributeFilter[],
	signal?: AbortSignal
): Promise<ServiceOverviewResponse> {
	const request = new GeneratedServiceOverviewRequest();
	request.windowMinutes = windowMinutes;
	request.resourceAttributes = toGeneratedResourceAttributes(resourceAttributes);

	const res = await apiFetch(`${API_BASE_URL}/api/services/overview`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedServiceOverviewRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/services/overview failed: ${res.status} ${res.statusText}`);
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

export interface ServiceDependencyGraph {
	windowMinutes: number;
	nodes: ServiceMapNode[];
	edges: ServiceMapEdge[];
}

// Deliberately reuses `service-map.ts`'s own `ServiceMapNode`/`ServiceMapEdge` shapes
// rather than declaring parallel ones here - see `ServiceDependencyQueryBuilder`'s remarks
// on the C# side for why the wire DTOs mirror them field-for-field: the aggregate Map view
// and the per-trace Service Map are the same data shape at a different scope, so they can
// share the one rendering component (`ServiceMapNode.svelte`) unmodified.
function toServiceMapNode(dto: GeneratedServiceDependencyNode): ServiceMapNode {
	return {
		service: dto.service ?? '',
		spanCount: Number(dto.spanCount),
		errorCount: Number(dto.errorCount),
		totalDurationNano: Number(dto.totalDurationNano),
		operations: (dto.topOperations ?? []).filter((op): op is string => op != null)
	};
}

function toServiceMapEdge(dto: GeneratedServiceDependencyEdge): ServiceMapEdge {
	return {
		source: dto.source ?? '',
		target: dto.target ?? '',
		callCount: Number(dto.callCount),
		totalDurationNano: Number(dto.totalDurationNano)
	};
}

/**
 * @param windowMinutes Lookback window, minutes. Server clamps/defaults - see `ServiceDependencyQueryBuilder.ClampWindowMinutes`.
 * @param resourceAttributes Optional filter chips, ANDed together server-side (both aliased sides of the edges query - see `ServiceDependencyQueryBuilder.Build`'s remarks). Omit/empty = no narrowing.
 */
export async function getServiceDependencyGraph(
	windowMinutes: number,
	resourceAttributes?: ResourceAttributeFilter[],
	signal?: AbortSignal
): Promise<ServiceDependencyGraph> {
	const request = new GeneratedServiceDependencyRequest();
	request.windowMinutes = windowMinutes;
	request.resourceAttributes = toGeneratedResourceAttributes(resourceAttributes);

	const res = await apiFetch(`${API_BASE_URL}/api/services/dependencies`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedServiceDependencyRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/services/dependencies failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedServiceDependencyGraphResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding ServiceDependencyGraphResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		nodes: (dto.nodes ?? []).map((n) => toServiceMapNode(n!)),
		edges: (dto.edges ?? []).map((e) => toServiceMapEdge(e!))
	};
}

export interface ExternalCallGroup {
	peerService: string;
	callCount: number;
	errorCount: number;
	errorRate: number;
	p50DurationMs: number;
	p95DurationMs: number;
}

export interface DatabaseCallGroup {
	dbSystem: string;
	dbOperation: string;
	callCount: number;
	errorCount: number;
	errorRate: number;
	p50DurationMs: number;
	p95DurationMs: number;
}

export interface ServiceCallBreakdown {
	service: string;
	windowMinutes: number;
	externalCalls: ExternalCallGroup[];
	databaseCalls: DatabaseCallGroup[];
}

function toExternalCallGroup(dto: GeneratedExternalCallGroup): ExternalCallGroup {
	return {
		peerService: dto.peerService ?? '',
		callCount: Number(dto.callCount),
		errorCount: Number(dto.errorCount),
		errorRate: dto.errorRate,
		p50DurationMs: dto.p50DurationMs,
		p95DurationMs: dto.p95DurationMs
	};
}

function toDatabaseCallGroup(dto: GeneratedDatabaseCallGroup): DatabaseCallGroup {
	return {
		dbSystem: dto.dbSystem ?? '',
		dbOperation: dto.dbOperation ?? '',
		callCount: Number(dto.callCount),
		errorCount: Number(dto.errorCount),
		errorRate: dto.errorRate,
		p50DurationMs: dto.p50DurationMs,
		p95DurationMs: dto.p95DurationMs
	};
}

/**
 * The Services tab Map view's per-node drill-down - "what does this service call, and how
 * slow/erroring is each one." @param service Exact service name (or `peer.service`-overridden
 * node id) clicked in the graph. @param windowMinutes Lookback window, minutes. Server
 * clamps/defaults - see `ServiceCallBreakdownQueryBuilder.ClampWindowMinutes`. @param
 * resourceAttributes Same filter chips as the Table/Map views, so a node's drill-down stays
 * consistent with whatever narrowed the graph it was opened from. Omit/empty = no narrowing.
 */
export async function getServiceCallBreakdown(
	service: string,
	windowMinutes: number,
	resourceAttributes?: ResourceAttributeFilter[],
	signal?: AbortSignal
): Promise<ServiceCallBreakdown> {
	const request = new GeneratedServiceCallBreakdownRequest();
	request.service = service;
	request.windowMinutes = windowMinutes;
	request.resourceAttributes = toGeneratedResourceAttributes(resourceAttributes);

	const res = await apiFetch(`${API_BASE_URL}/api/services/breakdown`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedServiceCallBreakdownRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/services/breakdown failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedServiceCallBreakdownResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding ServiceCallBreakdownResponse.');
	}
	return {
		service: dto.service ?? '',
		windowMinutes: dto.windowMinutes,
		externalCalls: (dto.externalCalls ?? []).map((c) => toExternalCallGroup(c!)),
		databaseCalls: (dto.databaseCalls ?? []).map((c) => toDatabaseCallGroup(c!))
	};
}
