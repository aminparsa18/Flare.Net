// Client for Flare.Api's /errors page Query API (grouped exceptions + per-group
// occurrences). Same MemoryPack shape as `traces-api.ts` - see that file's header comment
// for the general pattern. Every type here nests a `DateTimeOffset` somewhere
// (`ExceptionFilter.from`/`to`, `ExceptionGroup.firstSeen`/`lastSeen`,
// `ExceptionOccurrence.timestamp`), so all are hand-written (`$lib/memorypack/`).

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { ExceptionFilter as GeneratedExceptionFilter } from '$lib/memorypack/ExceptionFilter';
import { ExceptionGroupsRequest as GeneratedExceptionGroupsRequest } from '$lib/memorypack/ExceptionGroupsRequest';
import { ExceptionGroupsResponse as GeneratedExceptionGroupsResponse } from '$lib/memorypack/ExceptionGroupsResponse';
import type { ExceptionGroup as GeneratedExceptionGroup } from '$lib/memorypack/ExceptionGroup';
import { ExceptionOccurrencesRequest as GeneratedExceptionOccurrencesRequest } from '$lib/memorypack/ExceptionOccurrencesRequest';
import { ExceptionOccurrencesResponse as GeneratedExceptionOccurrencesResponse } from '$lib/memorypack/ExceptionOccurrencesResponse';
import type { ExceptionOccurrence as GeneratedExceptionOccurrence } from '$lib/memorypack/ExceptionOccurrence';

// ---- Shared filter shape (ErrorModels.cs's ExceptionFilter) ----------------

export interface ExceptionFilter {
	from?: string;
	to?: string;
	services?: string[];
}

function toGeneratedExceptionFilter(filter: ExceptionFilter | undefined): GeneratedExceptionFilter {
	const dto = new GeneratedExceptionFilter();
	if (filter == null) return dto;
	dto.from = filter.from == null ? null : new Date(filter.from);
	dto.to = filter.to == null ? null : new Date(filter.to);
	dto.services = filter.services ?? null;
	return dto;
}

// ---- POST /api/errors/groups (ExceptionGroupsRequest/Response) ------------

export interface ExceptionGroup {
	exceptionType: string;
	exceptionMessage: string;
	occurrenceCount: number;
	firstSeen: string;
	lastSeen: string;
	affectedServices: string[];
}

function toExceptionGroup(dto: GeneratedExceptionGroup): ExceptionGroup {
	return {
		exceptionType: dto.exceptionType ?? '',
		exceptionMessage: dto.exceptionMessage ?? '',
		occurrenceCount: Number(dto.occurrenceCount),
		firstSeen: dto.firstSeen.toISOString(),
		lastSeen: dto.lastSeen.toISOString(),
		affectedServices: (dto.affectedServices ?? []).filter((s): s is string => s != null)
	};
}

export interface ExceptionGroupsRequest {
	filter?: ExceptionFilter;
	topN?: number;
}

export interface ExceptionGroupsResponse {
	groups: ExceptionGroup[];
}

export async function getExceptionGroups(request: ExceptionGroupsRequest = {}, signal?: AbortSignal): Promise<ExceptionGroupsResponse> {
	const dto = new GeneratedExceptionGroupsRequest();
	dto.filter = toGeneratedExceptionFilter(request.filter);
	dto.topN = request.topN ?? null;
	const res = await apiFetch(`${API_BASE_URL}/api/errors/groups`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedExceptionGroupsRequest.serialize(dto)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/errors/groups failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedExceptionGroupsResponse.deserialize(await res.arrayBuffer());
	return {
		groups: (body?.groups ?? []).map((g) => toExceptionGroup(g!))
	};
}

// ---- POST /api/errors/occurrences (ExceptionOccurrencesRequest/Response) --
// One exception group's click-through drill-down - sample occurrences (trace/span to jump
// to, plus the stack trace) for one exact (exceptionType, exceptionMessage) pair.

export interface ExceptionOccurrence {
	traceId: string;
	spanId: string;
	serviceName: string;
	spanName: string;
	timestamp: string;
	stacktrace: string;
}

function toExceptionOccurrence(dto: GeneratedExceptionOccurrence): ExceptionOccurrence {
	return {
		traceId: dto.traceId ?? '',
		spanId: dto.spanId ?? '',
		serviceName: dto.serviceName ?? '',
		spanName: dto.spanName ?? '',
		timestamp: dto.timestamp.toISOString(),
		stacktrace: dto.stacktrace ?? ''
	};
}

export interface ExceptionOccurrencesRequest {
	filter?: ExceptionFilter;
	exceptionType: string;
	exceptionMessage: string;
}

export interface ExceptionOccurrencesResponse {
	exceptionType: string;
	exceptionMessage: string;
	occurrences: ExceptionOccurrence[];
}

export async function getExceptionOccurrences(
	request: ExceptionOccurrencesRequest,
	signal?: AbortSignal
): Promise<ExceptionOccurrencesResponse> {
	const dto = new GeneratedExceptionOccurrencesRequest();
	dto.filter = toGeneratedExceptionFilter(request.filter);
	dto.exceptionType = request.exceptionType;
	dto.exceptionMessage = request.exceptionMessage;
	const res = await apiFetch(`${API_BASE_URL}/api/errors/occurrences`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedExceptionOccurrencesRequest.serialize(dto)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/errors/occurrences failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedExceptionOccurrencesResponse.deserialize(await res.arrayBuffer());
	return {
		exceptionType: body?.exceptionType ?? request.exceptionType,
		exceptionMessage: body?.exceptionMessage ?? request.exceptionMessage,
		occurrences: (body?.occurrences ?? []).map((o) => toExceptionOccurrence(o!))
	};
}
