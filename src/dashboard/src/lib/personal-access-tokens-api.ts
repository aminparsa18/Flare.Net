// Client for Flare.Api's personal access token management (src/Flare.Api/Endpoints/
// PersonalAccessTokenEndpoints.cs, self-service - any authenticated user manages their
// own). See ADR-0019 for why this is a bearer credential resolved by the same
// SessionAuthenticationHandler every cookie-based call already goes through.
//
// MemoryPack throughout (see `ingest-keys-api.ts`'s header comment for the general
// shape): `CreateAccessTokenRequest` has no DateTimeOffset/JsonElement member, so it uses
// a real generated class (`$lib/generated/memorypack/CreateAccessTokenRequest.js`); every
// response nests `AccessTokenDto` (`CreatedAt`/`ExpiresAt`/`LastUsedAt`/`RevokedAt` are
// `DateTimeOffset`/`DateTimeOffset?`), so those are hand-written under `$lib/memorypack/`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import { CreateAccessTokenRequest as GeneratedCreateAccessTokenRequest } from '$lib/generated/memorypack/CreateAccessTokenRequest.js';
import { CreateAccessTokenResponse as GeneratedCreateAccessTokenResponse } from '$lib/memorypack/CreateAccessTokenResponse';
import { AccessTokenListResponse as GeneratedAccessTokenListResponse } from '$lib/memorypack/AccessTokenListResponse';
import type { AccessTokenDto as GeneratedAccessTokenDto } from '$lib/memorypack/AccessTokenDto';

export interface AccessToken {
	id: string;
	name: string;
	createdAt: string;
	expiresAt: string | null;
	lastUsedAt: string | null;
	revokedAt: string | null;
	isActive: boolean;
}

export interface CreateAccessTokenRequest {
	name: string;
	/** Omit (or null) for a token that never expires. */
	expiresInDays?: number | null;
}

/** `rawToken` is shown exactly once, here - Flare never stores or displays it again after this response. */
export interface CreateAccessTokenResponse {
	token: AccessToken;
	rawToken: string;
}

function toAccessToken(dto: GeneratedAccessTokenDto): AccessToken {
	return {
		id: dto.id,
		name: dto.name,
		createdAt: dto.createdAt.toISOString(),
		expiresAt: dto.expiresAt?.toISOString() ?? null,
		lastUsedAt: dto.lastUsedAt?.toISOString() ?? null,
		revokedAt: dto.revokedAt?.toISOString() ?? null,
		isActive: dto.isActive
	};
}

export async function createAccessToken(request: CreateAccessTokenRequest): Promise<CreateAccessTokenResponse> {
	const dto = new GeneratedCreateAccessTokenRequest();
	dto.name = request.name;
	dto.expiresInDays = request.expiresInDays ?? null;
	const res = await apiFetch(`${API_BASE_URL}/api/access-tokens`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedCreateAccessTokenRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/access-tokens failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedCreateAccessTokenResponse.deserialize(await res.arrayBuffer());
	if (body?.token == null || body.rawToken == null) {
		throw new Error('Empty response body decoding CreateAccessTokenResponse.');
	}
	return { token: toAccessToken(body.token), rawToken: body.rawToken };
}

/** The caller's own tokens only - see `PersonalAccessTokenEndpoints`'s remarks on why there's no "list every user's tokens" surface yet. */
export async function listAccessTokens(): Promise<AccessToken[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/access-tokens`, { headers: memoryPackAcceptHeaders() });
	if (!res.ok) {
		throw new Error(`GET /api/access-tokens failed: ${res.status} ${res.statusText}`);
	}
	const body = GeneratedAccessTokenListResponse.deserialize(await res.arrayBuffer());
	return (body?.tokens ?? []).filter((t): t is GeneratedAccessTokenDto => t != null).map(toAccessToken);
}

/** 204 No Content on success - no body to decode. */
export async function revokeAccessToken(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/access-tokens/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/access-tokens/${id} failed: ${res.status} ${res.statusText}`);
	}
}
