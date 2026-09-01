// Client for Flare.Api's Admin-only Entra ID settings API
// (src/Flare.Api/Endpoints/EntraSettingsEndpoints.cs).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape. Neither
// `EntraSettingsDto` nor `SaveEntraSettingsRequest` has an enum/DateTimeOffset member, so
// the generated classes' fields already match this module's exported interfaces one-for-one.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import { EntraSettingsDto } from '$lib/generated/memorypack/EntraSettingsDto.js';
import { SaveEntraSettingsRequest as GeneratedSaveEntraSettingsRequest } from '$lib/generated/memorypack/SaveEntraSettingsRequest.js';

export interface EntraSettings {
	enabled: boolean;
	tenantId: string | null;
	clientId: string | null;
	/** True once a client secret has been saved at least once - the real value is never
	 * returned (see EntraSettingsEndpoints.ToDto), same "will not be displayed once set"
	 * convention Seq's own Security page uses. */
	hasClientSecret: boolean;
	/** The exact redirect URI to register on the Entra App Registration - computed
	 * server-side from the current request's scheme+host. */
	redirectUri: string;
}

export interface SaveEntraSettingsRequest {
	enabled: boolean;
	tenantId: string | null;
	clientId: string | null;
	/** `null`/omitted leaves the currently-stored secret unchanged - only send a value
	 * when the Admin actually typed a new one. */
	clientSecret: string | null;
}

function toEntraSettings(dto: EntraSettingsDto): EntraSettings {
	return {
		enabled: dto.enabled,
		tenantId: dto.tenantId,
		clientId: dto.clientId,
		hasClientSecret: dto.hasClientSecret,
		redirectUri: dto.redirectUri ?? ''
	};
}

async function decodeEntraSettings(res: Response): Promise<EntraSettings> {
	const dto = EntraSettingsDto.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding EntraSettingsDto.');
	}
	return toEntraSettings(dto);
}

/** `GET /api/settings/entra`. */
export async function getEntraSettings(signal?: AbortSignal): Promise<EntraSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/entra`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/entra failed: ${res.status} ${res.statusText}`);
	}
	return decodeEntraSettings(res);
}

/** `PUT /api/settings/entra`. 400s if `enabled: true` is missing a tenant/client id or has no client secret on record - surfaced as a plain Error. */
export async function saveEntraSettings(request: SaveEntraSettingsRequest): Promise<EntraSettings> {
	const dto = new GeneratedSaveEntraSettingsRequest();
	dto.enabled = request.enabled;
	dto.tenantId = request.tenantId;
	dto.clientId = request.clientId;
	dto.clientSecret = request.clientSecret;
	const res = await apiFetch(`${API_BASE_URL}/api/settings/entra`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSaveEntraSettingsRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/entra failed: ${res.status} ${res.statusText}`);
	}
	return decodeEntraSettings(res);
}
