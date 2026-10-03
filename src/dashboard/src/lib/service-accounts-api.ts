// Client for Flare.Api's Admin-only service-account endpoints (src/Flare.Api/Endpoints/
// ServiceAccountEndpoints.cs, ADR-0082). Listing, role changes and disabling reuse
// `users-api.ts` (a service account is a user whose provider is "ServiceAccount"); token
// revocation reuses `revokeAccessToken` from `personal-access-tokens-api.ts`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import type { UserRole } from './auth-api';
import { userRoleFromString, userRoleToString } from '$lib/memorypack/enums';
import { UserSummaryDto } from '$lib/memorypack/UserSummaryDto';
import { CreateServiceAccountRequest } from '$lib/generated/memorypack/CreateServiceAccountRequest.js';
import { CreateAccessTokenRequest } from '$lib/generated/memorypack/CreateAccessTokenRequest.js';
import { CreateAccessTokenResponse } from '$lib/memorypack/CreateAccessTokenResponse';
import { AccessTokenListResponse } from '$lib/memorypack/AccessTokenListResponse';
import type { AccessTokenDto } from '$lib/memorypack/AccessTokenDto';
import type { UserSummary } from './users-api';
import type { AuthProvider } from './auth-api';
import type { AccessToken, CreateAccessTokenResponse as CreatedToken } from './personal-access-tokens-api';

function toAccessToken(dto: AccessTokenDto): AccessToken {
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

async function failure(res: Response, what: string): Promise<Error> {
	// 409 (duplicate name) and 400 carry a ProblemDetails `detail` worth showing.
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// Non-JSON body - fall back to the status text.
	}
	return new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

/** `POST /api/service-accounts`. */
export async function createServiceAccount(name: string, role: UserRole): Promise<UserSummary> {
	const request = new CreateServiceAccountRequest();
	request.name = name;
	request.role = userRoleFromString(role);
	const res = await apiFetch(`${API_BASE_URL}/api/service-accounts`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(CreateServiceAccountRequest.serialize(request))
	});
	if (!res.ok) throw await failure(res, 'POST /api/service-accounts');
	const dto = UserSummaryDto.deserialize(await res.arrayBuffer());
	if (dto == null) throw new Error('Empty response body decoding UserSummaryDto.');
	return {
		id: dto.id,
		username: dto.username,
		role: userRoleToString(dto.role),
		authProvider: dto.authProvider as AuthProvider,
		isDisabled: dto.isDisabled,
		createdAt: dto.createdAt.toISOString()
	};
}

/** `GET /api/service-accounts/{id}/access-tokens`. */
export async function listServiceAccountTokens(id: string): Promise<AccessToken[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/service-accounts/${id}/access-tokens`, { headers: memoryPackAcceptHeaders() });
	if (!res.ok) throw await failure(res, `GET /api/service-accounts/${id}/access-tokens`);
	const body = AccessTokenListResponse.deserialize(await res.arrayBuffer());
	return (body?.tokens ?? []).filter((t): t is AccessTokenDto => t != null).map(toAccessToken);
}

/** `POST /api/service-accounts/{id}/access-tokens`. `rawToken` is shown exactly once. */
export async function createServiceAccountToken(id: string, name: string, expiresInDays: number | null): Promise<CreatedToken> {
	const dto = new CreateAccessTokenRequest();
	dto.name = name;
	dto.expiresInDays = expiresInDays;
	const res = await apiFetch(`${API_BASE_URL}/api/service-accounts/${id}/access-tokens`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(CreateAccessTokenRequest.serialize(dto))
	});
	if (!res.ok) throw await failure(res, `POST /api/service-accounts/${id}/access-tokens`);
	const body = CreateAccessTokenResponse.deserialize(await res.arrayBuffer());
	if (body?.token == null || body.rawToken == null) throw new Error('Empty response body decoding CreateAccessTokenResponse.');
	return { token: toAccessToken(body.token), rawToken: body.rawToken };
}
