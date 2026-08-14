// Client for Flare.Api's Admin-only generic OpenID Connect settings API
// (src/Flare.Api/Endpoints/OidcSettingsEndpoints.cs). Field names/casing are a
// hand-mirror of src/Flare.Api/Model/OidcSettingsModels.cs + Json/OidcSettingsJsonContext.cs
// (camelCase properties, PascalCase string enum for `defaultRole`), same convention
// `entra-settings-api.ts`/`ldap-settings-api.ts` already document. Keep in sync with those
// files by hand.

import { API_BASE_URL, apiFetch } from './api';
import type { UserRole } from './auth-api';

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

/** `GET /api/settings/oidc`. */
export async function getOidcSettings(signal?: AbortSignal): Promise<OidcSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/oidc`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/oidc failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** `PUT /api/settings/oidc`. 400s if `enabled: true` is missing an authority/client id or has no client secret on record - surfaced as a plain Error. */
export async function saveOidcSettings(request: SaveOidcSettingsRequest): Promise<OidcSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/oidc`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/oidc failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
