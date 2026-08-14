// Client for Flare.Api's Admin-only Active Directory (LDAP) settings API
// (src/Flare.Api/Endpoints/LdapSettingsEndpoints.cs). Field names/casing are a
// hand-mirror of src/Flare.Api/Model/LdapSettingsModels.cs + Json/LdapSettingsJsonContext.cs,
// same convention `entra-settings-api.ts` already documents. Keep in sync with those
// files by hand.

import { API_BASE_URL, apiFetch } from './api';
import type { UserRole } from './auth-api';

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
}

/** `GET /api/settings/ldap`. */
export async function getLdapSettings(signal?: AbortSignal): Promise<LdapSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/ldap`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/ldap failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** `PUT /api/settings/ldap`. 400s if `enabled: true` is missing Host/Base DN/Bind DN or has no bind password on record - surfaced as a plain Error. */
export async function saveLdapSettings(request: SaveLdapSettingsRequest): Promise<LdapSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/ldap`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/ldap failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
