// Client for Flare.Api's global auth on/off switch (src/Flare.Api/Endpoints/AuthSettingsEndpoints.cs).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape. `AuthSettingsDto`
// has no enum/DateTimeOffset member, so the generated class's fields already match this
// module's exported `AuthSettings` interface one-for-one.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import { AuthSettingsDto } from '$lib/generated/memorypack/AuthSettingsDto.js';

export interface AuthSettings {
	enabled: boolean;
	localEnabled: boolean;
}

function toAuthSettings(dto: AuthSettingsDto): AuthSettings {
	return { enabled: dto.enabled, localEnabled: dto.localEnabled };
}

async function decodeAuthSettings(res: Response): Promise<AuthSettings> {
	const dto = AuthSettingsDto.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding AuthSettingsDto.');
	}
	return toAuthSettings(dto);
}

/** `GET /api/settings/auth`. Reachable unauthenticated while `enabled` is false - see AuthSettingsEndpoints' remarks. */
export async function getAuthSettings(signal?: AbortSignal): Promise<AuthSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/auth`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/auth failed: ${res.status} ${res.statusText}`);
	}
	return decodeAuthSettings(res);
}

/** `PUT /api/settings/auth`. 400s if `enabled: true` has no usable sign-in method - same generic status-text Error shape every other `-api.ts` module here uses, not the ProblemDetails body. */
export async function saveAuthSettings(settings: AuthSettings): Promise<AuthSettings> {
	const request = new AuthSettingsDto();
	request.enabled = settings.enabled;
	request.localEnabled = settings.localEnabled;
	const res = await apiFetch(`${API_BASE_URL}/api/settings/auth`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(AuthSettingsDto.serialize(request))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/auth failed: ${res.status} ${res.statusText}`);
	}
	return decodeAuthSettings(res);
}
