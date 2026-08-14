// Client for Flare.Api's Admin-only reverse-proxy (trusted header) settings API
// (src/Flare.Api/Endpoints/ProxyAuthSettingsEndpoints.cs). Field names/casing are a
// hand-mirror of src/Flare.Api/Model/ProxyAuthModels.cs + Json/ProxyAuthJsonContext.cs,
// same convention `ldap-settings-api.ts`/`oidc-settings-api.ts` already document. Keep in
// sync with those files by hand.

import { API_BASE_URL, apiFetch } from './api';
import type { UserRole } from './auth-api';

export interface ProxyAuthSettings {
	enabled: boolean;
	headerName: string;
	/** One or more CIDR entries, newline/comma-separated - the entire trust boundary for
	 * this method (see docs/auth.md). No default of "trust everyone" exists server-side;
	 * this can't be blank while `enabled` is true. */
	trustedProxyCidrs: string;
	groupsHeaderName: string | null;
	adminGroup: string | null;
	memberGroup: string | null;
	viewerGroup: string | null;
	defaultRole: UserRole;
}

export interface SaveProxyAuthSettingsRequest {
	enabled: boolean;
	headerName: string;
	trustedProxyCidrs: string;
	groupsHeaderName: string | null;
	adminGroup: string | null;
	memberGroup: string | null;
	viewerGroup: string | null;
	defaultRole: UserRole;
}

/** `GET /api/settings/proxyauth`. */
export async function getProxyAuthSettings(signal?: AbortSignal): Promise<ProxyAuthSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/proxyauth`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/proxyauth failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** `PUT /api/settings/proxyauth`. 400s if `enabled: true` has a blank header name or no CIDR entry that actually parses - surfaced as a plain Error. */
export async function saveProxyAuthSettings(request: SaveProxyAuthSettingsRequest): Promise<ProxyAuthSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/proxyauth`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/proxyauth failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
