// Client for Flare.Api's global auth on/off switch (src/Flare.Api/Endpoints/AuthSettingsEndpoints.cs).
// Field names/casing are a hand-mirror of src/Flare.Api/Model/AuthSettingsModels.cs +
// Json/AuthSettingsJsonContext.cs (camelCase properties), same convention
// `entra-settings-api.ts` already documents.

import { API_BASE_URL, apiFetch } from './api';

export interface AuthSettings {
	enabled: boolean;
	localEnabled: boolean;
}

/** `GET /api/settings/auth`. Reachable unauthenticated while `enabled` is false - see AuthSettingsEndpoints' remarks. */
export async function getAuthSettings(signal?: AbortSignal): Promise<AuthSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/auth`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/auth failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** `PUT /api/settings/auth`. 400s if `enabled: true` has no usable sign-in method - same generic status-text Error shape every other `-api.ts` module here uses, not the ProblemDetails body. */
export async function saveAuthSettings(settings: AuthSettings): Promise<AuthSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/auth`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(settings)
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/auth failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
