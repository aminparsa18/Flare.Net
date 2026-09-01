// Client for Flare.Api's ingest API-key management (src/Flare.Api/Endpoints/
// IngestApiKeyEndpoints.cs, admin-only).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape.
// `CreateIngestApiKeyRequest` has no DateTimeOffset/JsonElement member, so it uses a real
// generated class; the response, `CreateIngestApiKeyResponse`, nests `IngestApiKeyDto`
// (`CreatedAt`/`RevokedAt` are `DateTimeOffset`/`DateTimeOffset?`), so both are hand-written
// (`$lib/memorypack/IngestApiKeyDto.ts`/`CreateIngestApiKeyResponse.ts`).
//
// Only create() is wrapped here, matching Flare.Cli's own deliberately-scoped
// ApiKeyCreateCommand - GET /api/ingest-keys and DELETE /api/ingest-keys/{id} exist on
// the backend but aren't exposed from either front end yet.

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { CreateIngestApiKeyRequest as GeneratedCreateIngestApiKeyRequest } from '$lib/generated/memorypack/CreateIngestApiKeyRequest.js';
import { CreateIngestApiKeyResponse as GeneratedCreateIngestApiKeyResponse } from '$lib/memorypack/CreateIngestApiKeyResponse';

export interface IngestApiKeyDto {
	id: string;
	name: string;
	createdAt: string;
	revokedAt: string | null;
	isActive: boolean;
}

export interface CreateIngestApiKeyRequest {
	name: string;
}

/** `rawKey` is shown exactly once, here - Flare never stores or displays it again after this response. */
export interface CreateIngestApiKeyResponse {
	key: IngestApiKeyDto;
	rawKey: string;
}

export async function createIngestApiKey(request: CreateIngestApiKeyRequest): Promise<CreateIngestApiKeyResponse> {
	const dto = new GeneratedCreateIngestApiKeyRequest();
	dto.name = request.name;
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
	return {
		key: {
			id: body.key.id,
			name: body.key.name,
			createdAt: body.key.createdAt.toISOString(),
			revokedAt: body.key.revokedAt?.toISOString() ?? null,
			isActive: body.key.isActive
		},
		rawKey: body.rawKey
	};
}
