// Client for Flare.Api's ingest API-key management (src/Flare.Api/Endpoints/
// IngestApiKeyEndpoints.cs, admin-only).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape.
// `CreateIngestApiKeyRequest`/`UpdateIngestApiKeyLimitsRequest` have no
// DateTimeOffset/JsonElement member, so they use real generated classes; every response
// nests `IngestApiKeyDto` (`CreatedAt`/`RevokedAt` are `DateTimeOffset`/`DateTimeOffset?`),
// so those are hand-written under `$lib/memorypack/`.
//
// Used by the Ingest Keys page (routes/ingest-keys, ADR-0051's limits + usage) and by the
// terminal's `apikey create` command.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import { CreateIngestApiKeyRequest as GeneratedCreateIngestApiKeyRequest } from '$lib/generated/memorypack/CreateIngestApiKeyRequest.js';
import { SetIngestApiKeyProjectRequest as GeneratedSetIngestApiKeyProjectRequest } from '$lib/generated/memorypack/SetIngestApiKeyProjectRequest.js';
import { NO_PROJECT } from './projects-api';
import { SetIngestApiKeyOriginsRequest as GeneratedSetIngestApiKeyOriginsRequest } from '$lib/generated/memorypack/SetIngestApiKeyOriginsRequest.js';
import { SetIngestApiKeyServicesRequest as GeneratedSetIngestApiKeyServicesRequest } from '$lib/generated/memorypack/SetIngestApiKeyServicesRequest.js';
import { UpdateIngestApiKeyLimitsRequest as GeneratedUpdateIngestApiKeyLimitsRequest } from '$lib/generated/memorypack/UpdateIngestApiKeyLimitsRequest.js';
import { CreateIngestApiKeyResponse as GeneratedCreateIngestApiKeyResponse } from '$lib/memorypack/CreateIngestApiKeyResponse';
import { IngestApiKeyListResponse as GeneratedIngestApiKeyListResponse } from '$lib/memorypack/IngestApiKeyListResponse';
import type { IngestApiKeyDto as GeneratedIngestApiKeyDto } from '$lib/memorypack/IngestApiKeyDto';

/** Per-key caps (ADR-0051). A null cap means "no cap on that dimension". */
export interface IngestApiKeyLimits {
	limitsEnabled: boolean;
	maxEventsPerMinute: number | null;
	maxBytesPerMinute: number | null;
	maxEventsPerDay: number | null;
	maxBytesPerDay: number | null;
}

/** Usage in the current UTC minute / UTC day - the same Redis counters the limits are enforced against. */
export interface IngestApiKeyUsage {
	eventsThisMinute: number;
	bytesThisMinute: number;
	eventsToday: number;
	bytesToday: number;
}

export interface IngestApiKeyDto extends IngestApiKeyLimits, IngestApiKeyUsage {
	id: string;
	name: string;
	createdAt: string;
	revokedAt: string | null;
	isActive: boolean;
	/** Owning project (ADR-0123); `null` = instance-wide. */
	projectId: string | null;
	/** Browser origins the key is restricted to (ADR-0149); empty = unrestricted. */
	allowedOrigins: string[];
	/** service.name values the key may write for (ADR-0150); empty = any. */
	allowedServices: string[];
}

export interface CreateIngestApiKeyRequest {
	name: string;
	projectId?: string | null;
}

/** `rawKey` is shown exactly once, here - Flare never stores or displays it again after this response. */
export interface CreateIngestApiKeyResponse {
	key: IngestApiKeyDto;
	rawKey: string;
}

// Counts/bytes stay far below Number.MAX_SAFE_INTEGER (~9 PB), so converting the wire's
// bigint to number for display/inputs loses nothing in practice.
const toNumber = (v: bigint | null): number | null => (v == null ? null : Number(v));
const toBigInt = (v: number | null): bigint | null => (v == null ? null : BigInt(Math.trunc(v)));

function toIngestApiKey(dto: GeneratedIngestApiKeyDto): IngestApiKeyDto {
	return {
		id: dto.id,
		name: dto.name,
		createdAt: dto.createdAt.toISOString(),
		revokedAt: dto.revokedAt?.toISOString() ?? null,
		isActive: dto.isActive,
		projectId: dto.projectId,
		allowedOrigins: dto.allowedOrigins,
		allowedServices: dto.allowedServices,
		limitsEnabled: dto.limitsEnabled,
		maxEventsPerMinute: toNumber(dto.maxEventsPerMinute),
		maxBytesPerMinute: toNumber(dto.maxBytesPerMinute),
		maxEventsPerDay: toNumber(dto.maxEventsPerDay),
		maxBytesPerDay: toNumber(dto.maxBytesPerDay),
		eventsThisMinute: Number(dto.eventsThisMinute),
		bytesThisMinute: Number(dto.bytesThisMinute),
		eventsToday: Number(dto.eventsToday),
		bytesToday: Number(dto.bytesToday)
	};
}

export async function createIngestApiKey(request: CreateIngestApiKeyRequest): Promise<CreateIngestApiKeyResponse> {
	const dto = new GeneratedCreateIngestApiKeyRequest();
	dto.name = request.name;
	dto.projectId = request.projectId ?? null;
	const res = await apiFetch(`${API_BASE_URL}/api/ingest-keys`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedCreateIngestApiKeyRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/ingest-keys failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedCreateIngestApiKeyResponse.deserialize(await res.arrayBuffer());
	if (body?.key == null || body.rawKey == null) {
		throw new Error('Empty response body decoding CreateIngestApiKeyResponse.');
	}
	return { key: toIngestApiKey(body.key), rawKey: body.rawKey };
}

export async function listIngestApiKeys(): Promise<IngestApiKeyDto[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/ingest-keys`, { headers: memoryPackAcceptHeaders() });
	if (!res.ok) {
		throw new Error(`GET /api/ingest-keys failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedIngestApiKeyListResponse.deserialize(await res.arrayBuffer());
	return (body?.keys ?? []).filter((k): k is GeneratedIngestApiKeyDto => k != null).map(toIngestApiKey);
}

/** 204 No Content on success - no body to decode. */
export async function revokeIngestApiKey(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/ingest-keys/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/ingest-keys/${id} failed: ${res.status} ${res.statusText}`);
	}
}

/** 204 No Content on success. `null` moves the key back to instance-wide (ADR-0123). */
export async function setIngestApiKeyProject(id: string, projectId: string | null): Promise<void> {
	const dto = new GeneratedSetIngestApiKeyProjectRequest();
	dto.projectId = projectId ?? NO_PROJECT;
	const res = await apiFetch(`${API_BASE_URL}/api/ingest-keys/${id}/project`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSetIngestApiKeyProjectRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/ingest-keys/${id}/project failed: ${res.status} ${res.statusText}`);
	}
}

/** 204 No Content on success. Reaches Flare.Ingest within its key cache's 30s refresh. */
export async function updateIngestApiKeyLimits(id: string, limits: IngestApiKeyLimits): Promise<void> {
	const dto = new GeneratedUpdateIngestApiKeyLimitsRequest();
	dto.limitsEnabled = limits.limitsEnabled;
	dto.maxEventsPerMinute = toBigInt(limits.maxEventsPerMinute);
	dto.maxBytesPerMinute = toBigInt(limits.maxBytesPerMinute);
	dto.maxEventsPerDay = toBigInt(limits.maxEventsPerDay);
	dto.maxBytesPerDay = toBigInt(limits.maxBytesPerDay);
	const res = await apiFetch(`${API_BASE_URL}/api/ingest-keys/${id}/limits`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedUpdateIngestApiKeyLimitsRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/ingest-keys/${id}/limits failed: ${res.status} ${res.statusText}`);
	}
}

/** Surfaces the API's problem-details message (e.g. which origin was malformed) over a bare status line. */
async function allowlistError(res: Response, what: string): Promise<Error> {
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// non-JSON body: fall back to the status line
	}
	return new Error(detail || `PUT ${what} failed: ${res.status} ${res.statusText}`);
}

/** 204 No Content on success. An empty list lifts the restriction (ADR-0149). */
export async function setIngestApiKeyOrigins(id: string, origins: string[]): Promise<void> {
	const dto = new GeneratedSetIngestApiKeyOriginsRequest();
	dto.origins = origins;
	const res = await apiFetch(`${API_BASE_URL}/api/ingest-keys/${id}/origins`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSetIngestApiKeyOriginsRequest.serialize(dto))
	});
	if (!res.ok) throw await allowlistError(res, `/api/ingest-keys/${id}/origins`);
}

/** 204 No Content on success. An empty list lifts the restriction (ADR-0150). */
export async function setIngestApiKeyServices(id: string, services: string[]): Promise<void> {
	const dto = new GeneratedSetIngestApiKeyServicesRequest();
	dto.services = services;
	const res = await apiFetch(`${API_BASE_URL}/api/ingest-keys/${id}/services`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSetIngestApiKeyServicesRequest.serialize(dto))
	});
	if (!res.ok) throw await allowlistError(res, `/api/ingest-keys/${id}/services`);
}
