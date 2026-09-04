// Client for Flare.Api's Admin-only Active Directory (LDAP) settings API
// (src/Flare.Api/Endpoints/LdapSettingsEndpoints.cs).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape, and
// `$lib/memorypack/enums.ts` for why `defaultRole` converts through `userRoleToString`/
// `userRoleFromString` instead of being passed through as MemoryPack's raw numeric ordinal.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import type { UserRole } from './auth-api';
import { userRoleFromString, userRoleToString } from '$lib/memorypack/enums';
import { LdapSettingsDto } from '$lib/generated/memorypack/LdapSettingsDto.js';
import { SaveLdapSettingsRequest as GeneratedSaveLdapSettingsRequest } from '$lib/generated/memorypack/SaveLdapSettingsRequest.js';

export interface LdapSettings {
	enabled: boolean;
	host: string | null;
	port: number;
	useSsl: boolean;
	baseDn: string | null;
	bindDn: string | null;
	/** True once a bind password has been saved at least once - the real value is never
	 * returned, same "will not be displayed once set" convention `EntraSettings.hasClientSecret` uses. */
	hasBindPassword: boolean;
	userSearchFilter: string;
	uniqueIdAttribute: string;
	adminGroupDn: string | null;
	memberGroupDn: string | null;
	viewerGroupDn: string | null;
	defaultRole: UserRole;
	/** PEM-encoded certificate pinned as the sole TLS trust anchor for LDAP connections -
	 * unlike `hasBindPassword`, echoed back in full (not redacted): a certificate isn't a
	 * secret. `null` means no pin is configured (falls back to the OS/container trust store). */
	pinnedCertificatePem: string | null;
}

export interface SaveLdapSettingsRequest {
	enabled: boolean;
	host: string | null;
	port: number;
	useSsl: boolean;
	baseDn: string | null;
	bindDn: string | null;
	/** `null`/omitted leaves the currently-stored password unchanged - only send a value
	 * when the Admin actually typed a new one. */
	bindPassword: string | null;
	userSearchFilter: string;
	uniqueIdAttribute: string;
	adminGroupDn: string | null;
	memberGroupDn: string | null;
	viewerGroupDn: string | null;
	defaultRole: UserRole;
	/** `null`/omitted always *clears* any previously-saved pin - unlike `bindPassword`,
	 * there's no "leave unchanged" semantics for this field (it isn't a secret). */
	pinnedCertificatePem: string | null;
}

function toLdapSettings(dto: LdapSettingsDto): LdapSettings {
	return {
		enabled: dto.enabled,
		host: dto.host,
		port: dto.port,
		useSsl: dto.useSsl,
		baseDn: dto.baseDn,
		bindDn: dto.bindDn,
		hasBindPassword: dto.hasBindPassword,
		userSearchFilter: dto.userSearchFilter ?? '',
		uniqueIdAttribute: dto.uniqueIdAttribute ?? '',
		adminGroupDn: dto.adminGroupDn,
		memberGroupDn: dto.memberGroupDn,
		viewerGroupDn: dto.viewerGroupDn,
		defaultRole: userRoleToString(dto.defaultRole),
		pinnedCertificatePem: dto.pinnedCertificatePem
	};
}

async function decodeLdapSettings(res: Response): Promise<LdapSettings> {
	const dto = LdapSettingsDto.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding LdapSettingsDto.');
	}
	return toLdapSettings(dto);
}

/** `GET /api/settings/ldap`. */
export async function getLdapSettings(signal?: AbortSignal): Promise<LdapSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/ldap`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/ldap failed: ${res.status} ${res.statusText}`);
	}
	return decodeLdapSettings(res);
}

/** `PUT /api/settings/ldap`. 400s if `enabled: true` is missing Host/Base DN/Bind DN or has no bind password on record - surfaced as a plain Error. */
export async function saveLdapSettings(request: SaveLdapSettingsRequest): Promise<LdapSettings> {
	const dto = new GeneratedSaveLdapSettingsRequest();
	dto.enabled = request.enabled;
	dto.host = request.host;
	dto.port = request.port;
	dto.useSsl = request.useSsl;
	dto.baseDn = request.baseDn;
	dto.bindDn = request.bindDn;
	dto.bindPassword = request.bindPassword;
	dto.userSearchFilter = request.userSearchFilter;
	dto.uniqueIdAttribute = request.uniqueIdAttribute;
	dto.adminGroupDn = request.adminGroupDn;
	dto.memberGroupDn = request.memberGroupDn;
	dto.viewerGroupDn = request.viewerGroupDn;
	dto.defaultRole = userRoleFromString(request.defaultRole);
	dto.pinnedCertificatePem = request.pinnedCertificatePem;
	const res = await apiFetch(`${API_BASE_URL}/api/settings/ldap`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSaveLdapSettingsRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/ldap failed: ${res.status} ${res.statusText}`);
	}
	return decodeLdapSettings(res);
}
