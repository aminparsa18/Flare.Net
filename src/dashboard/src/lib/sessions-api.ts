// Client for Flare.Api's self-service session management (src/Flare.Api/Endpoints/
// SessionEndpoints.cs). Plain JSON: small, rarely-called endpoints don't need MemoryPack.
// Session ids here are one-way handles, never the session token itself.

import { API_BASE_URL, apiFetch } from './api';

export interface UserSession {
	id: string;
	createdAt: string;
	lastSeenAt: string;
	expiresAt: string;
	/** True for the session making the request. */
	isCurrent: boolean;
}

export async function listSessions(signal?: AbortSignal): Promise<UserSession[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/sessions`, { signal });
	if (!res.ok) throw new Error(`GET /api/auth/sessions failed: ${res.status} ${res.statusText}`);
	return ((await res.json()) as { sessions: UserSession[] }).sessions;
}

export async function revokeSession(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/sessions/${encodeURIComponent(id)}`, { method: 'DELETE' });
	if (!res.ok && res.status !== 404) throw new Error(`DELETE /api/auth/sessions failed: ${res.status} ${res.statusText}`);
}

/** Signs out every session; `keepCurrent` spares the calling one. */
export async function revokeAllSessions(keepCurrent: boolean): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/sessions?keepCurrent=${keepCurrent}`, { method: 'DELETE' });
	if (!res.ok) throw new Error(`DELETE /api/auth/sessions failed: ${res.status} ${res.statusText}`);
}
