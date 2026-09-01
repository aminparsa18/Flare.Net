// Client for Flare.Api's Admin-only user-management API (src/Flare.Api/Endpoints/UserEndpoints.cs).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape.
// `SetUserRoleRequest`/`SetUserDisabledRequest` have no DateTimeOffset/JsonElement member,
// so they use real generated classes; the response type, `UserSummaryDto`/`UserListResponse`,
// has a `DateTimeOffset CreatedAt` field MemoryPack's TypeScript generator can't map (see
// `$lib/memorypack/UserSummaryDto.ts`'s header comment), so those two are hand-written
// instead - the first fully migrated "blocked" file, proving that pattern end-to-end
// against a live server (see the investigation doc's Phase 2 live e2e section).

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import type { AuthProvider, UserRole } from './auth-api';
import { userRoleFromString, userRoleToString } from '$lib/memorypack/enums';
import { UserSummaryDto } from '$lib/memorypack/UserSummaryDto';
import { UserListResponse } from '$lib/memorypack/UserListResponse';
import { SetUserRoleRequest } from '$lib/generated/memorypack/SetUserRoleRequest.js';
import { SetUserDisabledRequest } from '$lib/generated/memorypack/SetUserDisabledRequest.js';

export interface UserSummary {
	id: string;
	username: string;
	role: UserRole;
	authProvider: AuthProvider;
	isDisabled: boolean;
	createdAt: string;
}

function toUserSummary(dto: UserSummaryDto): UserSummary {
	return {
		id: dto.id,
		username: dto.username,
		role: userRoleToString(dto.role),
		authProvider: dto.authProvider as AuthProvider,
		isDisabled: dto.isDisabled,
		createdAt: dto.createdAt.toISOString()
	};
}

async function decodeUserSummary(res: Response): Promise<UserSummary> {
	const dto = UserSummaryDto.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding UserSummaryDto.');
	}
	return toUserSummary(dto);
}

/** `GET /api/users`. */
export async function listUsers(signal?: AbortSignal): Promise<UserSummary[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/users`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/users failed: ${res.status} ${res.statusText}`);
	}
	const body = UserListResponse.deserialize(await res.arrayBuffer());
	return (body?.users ?? []).map((dto) => toUserSummary(dto!));
}

/** `PATCH /api/users/{id}/role`. 400s if this would demote the last enabled Admin - same generic status-text Error shape every other `-api.ts` module in this app uses, not the ProblemDetails body (no existing precedent here for parsing it). */
export async function setUserRole(id: string, role: UserRole): Promise<UserSummary> {
	const request = new SetUserRoleRequest();
	request.role = userRoleFromString(role);
	const res = await apiFetch(`${API_BASE_URL}/api/users/${id}/role`, {
		method: 'PATCH',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(SetUserRoleRequest.serialize(request))
	});
	if (!res.ok) {
		throw new Error(`PATCH /api/users/${id}/role failed: ${res.status} ${res.statusText}`);
	}
	return decodeUserSummary(res);
}

/** `PATCH /api/users/{id}/disabled`. 400s if this would disable the last enabled Admin. */
export async function setUserDisabled(id: string, isDisabled: boolean): Promise<UserSummary> {
	const request = new SetUserDisabledRequest();
	request.isDisabled = isDisabled;
	const res = await apiFetch(`${API_BASE_URL}/api/users/${id}/disabled`, {
		method: 'PATCH',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(SetUserDisabledRequest.serialize(request))
	});
	if (!res.ok) {
		throw new Error(`PATCH /api/users/${id}/disabled failed: ${res.status} ${res.statusText}`);
	}
	return decodeUserSummary(res);
}
