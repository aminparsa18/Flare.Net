// Client for Flare.Api's Admin-only generic OpenID Connect settings API
// (src/Flare.Api/Endpoints/OidcSettingsEndpoints.cs).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape, and
// `$lib/memorypack/enums.ts` for why `defaultRole` converts through `userRoleToString`/
// `userRoleFromString` instead of being passed through as MemoryPack's raw numeric ordinal.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import type { UserRole } from './auth-api';
import { userRoleFromString, userRoleToString } from '$lib/memorypack/enums';
import { OidcSettingsDto } from '$lib/generated/memorypack/OidcSettingsDto.js';
import { SaveOidcSettingsRequest as GeneratedSaveOidcSettingsRequest } from '$lib/generated/memorypack/SaveOidcSettingsRequest.js';

export interface OidcSettings {
	enabled: boolean;
	/** Drives the /login page's "Sign in with {displayName}" button label - a generic
	 * provider has no fixed brand the way Entra's "Microsoft" does. */
	displayName: string | null;
	authority: string | null;
	clientId: string | null;
	/** True once a client secret has been saved at least once - the real value is never
	 * returned, same "will not be displayed once set" convention `EntraSettings.hasClientSecret` uses. */
	hasClientSecret: boolean;
	scopes: string;
	roleClaimName: string;
	defaultRole: UserRole;
	/** The exact callback URI to register with the OIDC provider - computed server-side
	 * from the current request's scheme+host. */
	redirectUri: string;
}

export interface SaveOidcSettingsRequest {
	enabled: boolean;
	displayName: string | null;
	authority: string | null;
	clientId: string | null;
	/** `null`/omitted leaves the currently-stored secret unchanged - only send a value
	 * when the Admin actually typed a new one. */
	clientSecret: string | null;
	scopes: string;
	roleClaimName: string;
	defaultRole: UserRole;
}

function toOidcSettings(dto: OidcSettingsDto): OidcSettings {
	return {
		enabled: dto.enabled,
		displayName: dto.displayName,
		authority: dto.authority,
		clientId: dto.clientId,
		hasClientSecret: dto.hasClientSecret,
		scopes: dto.scopes ?? '',
		roleClaimName: dto.roleClaimName ?? '',
		defaultRole: userRoleToString(dto.defaultRole),
		redirectUri: dto.redirectUri ?? ''
	};
}

async function decodeOidcSettings(res: Response): Promise<OidcSettings> {
	const dto = OidcSettingsDto.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding OidcSettingsDto.');
	}
	return toOidcSettings(dto);
}

/** `GET /api/settings/oidc`. */
export async function getOidcSettings(signal?: AbortSignal): Promise<OidcSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/oidc`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/oidc failed: ${res.status} ${res.statusText}`);
	}
	return decodeOidcSettings(res);
}

/** `PUT /api/settings/oidc`. 400s if `enabled: true` is missing an authority/client id or has no client secret on record - surfaced as a plain Error. */
export async function saveOidcSettings(request: SaveOidcSettingsRequest): Promise<OidcSettings> {
	const dto = new GeneratedSaveOidcSettingsRequest();
	dto.enabled = request.enabled;
	dto.displayName = request.displayName;
	dto.authority = request.authority;
	dto.clientId = request.clientId;
	dto.clientSecret = request.clientSecret;
	dto.scopes = request.scopes;
	dto.roleClaimName = request.roleClaimName;
	dto.defaultRole = userRoleFromString(request.defaultRole);
	const res = await apiFetch(`${API_BASE_URL}/api/settings/oidc`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSaveOidcSettingsRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/oidc failed: ${res.status} ${res.statusText}`);
	}
	return decodeOidcSettings(res);
}
