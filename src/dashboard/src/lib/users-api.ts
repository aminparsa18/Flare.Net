// Client for Flare.Api's Admin-only user-management API (src/Flare.Api/Endpoints/UserEndpoints.cs).
// Field names/casing are a hand-mirror of src/Flare.Api/Model/UserModels.cs +
// Json/UsersJsonContext.cs (camelCase properties, PascalCase string enum for `role` -
// same convention `auth-api.ts` already documents). Keep in sync with those files by hand.

import { API_BASE_URL, apiFetch } from './api';
import type { AuthProvider, UserRole } from './auth-api';

export interface UserSummary {
	id: string;
	username: string;
	role: UserRole;
	authProvider: AuthProvider;
	isDisabled: boolean;
	createdAt: string;
}

/** `GET /api/users`. */
export async function listUsers(signal?: AbortSignal): Promise<UserSummary[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/users`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/users failed: ${res.status} ${res.statusText}`);
	}
	const body: { users: UserSummary[] } = await res.json();
	return body.users;
}

/** `PATCH /api/users/{id}/role`. 400s if this would demote the last enabled Admin - same generic status-text Error shape every other `-api.ts` module in this app uses, not the ProblemDetails body (no existing precedent here for parsing it). */
export async function setUserRole(id: string, role: UserRole): Promise<UserSummary> {
	const res = await apiFetch(`${API_BASE_URL}/api/users/${id}/role`, {
		method: 'PATCH',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ role })
	});
	if (!res.ok) {
		throw new Error(`PATCH /api/users/${id}/role failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** `PATCH /api/users/{id}/disabled`. 400s if this would disable the last enabled Admin. */
export async function setUserDisabled(id: string, isDisabled: boolean): Promise<UserSummary> {
	const res = await apiFetch(`${API_BASE_URL}/api/users/${id}/disabled`, {
		method: 'PATCH',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ isDisabled })
	});
	if (!res.ok) {
		throw new Error(`PATCH /api/users/${id}/disabled failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
