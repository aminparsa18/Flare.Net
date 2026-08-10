// Client for Flare.Api's Admin-only Entra ID settings API
// (src/Flare.Api/Endpoints/EntraSettingsEndpoints.cs). Field names/casing are a
// hand-mirror of src/Flare.Api/Model/EntraSettingsModels.cs + Json/EntraSettingsJsonContext.cs
// (camelCase properties, same convention `users-api.ts` already documents). Keep in sync
// with those files by hand.

import { API_BASE_URL, apiFetch } from './api';

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

/** `GET /api/settings/entra`. */
export async function getEntraSettings(signal?: AbortSignal): Promise<EntraSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/entra`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/entra failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** `PUT /api/settings/entra`. 400s if `enabled: true` is missing a tenant/client id or has no client secret on record - surfaced as a plain Error. */
export async function saveEntraSettings(request: SaveEntraSettingsRequest): Promise<EntraSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/entra`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/entra failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
