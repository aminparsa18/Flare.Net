// Client for Flare.Api's auth endpoints (src/Flare.Api/Endpoints/AuthEndpoints.cs).
// Field names/casing are a hand-mirror of src/Flare.Api/Model/AuthModels.cs +
// Json/AuthJsonContext.cs (camelCase properties, PascalCase string enum for `role` -
// `UseStringEnumConverter` with no naming policy, same convention as $lib/api.ts's
// LogTailClientMessage/LogTailServerMessage). Keep in sync with those files by hand.

import { API_BASE_URL, apiFetch } from './api';

export type UserRole = 'Admin' | 'Member' | 'Viewer';

export type AuthProvider = 'Local' | 'Entra';

export interface AuthUser {
	id: string;
	username: string;
	role: UserRole;
	authProvider: AuthProvider;
}

export interface BootstrapStatusResponse {
	needsBootstrap: boolean;
	/** Whether Flare.Api has Auth:Entra:Enabled=true - gates the "Sign in with
	 * Microsoft" button on /login (see EntraAuthEndpoints.HandleLoginAsync, which
	 * itself 404s when this is false - the button check is a UX nicety on top of that,
	 * not the actual enforcement). */
	entraEnabled: boolean;
}

/** `GET /api/auth/bootstrap/status` - decides whether `+layout.svelte`'s route guard sends an unauthenticated visitor to `/setup` or `/login`. */
export async function getBootstrapStatus(signal?: AbortSignal): Promise<BootstrapStatusResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/bootstrap/status`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/auth/bootstrap/status failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** `POST /api/auth/bootstrap` - first-run only, creates the first Admin and signs them in. 409s (surfaced as a plain Error) if an admin already exists. */
export async function bootstrap(username: string, password: string): Promise<AuthUser> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/bootstrap`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ username, password })
	});
	if (res.status === 409) {
		throw new Error('An admin user already exists.');
	}
	if (!res.ok) {
		throw new Error(`POST /api/auth/bootstrap failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** `POST /api/auth/login`. A 401 (wrong username/password, or an unknown username - AuthEndpoints deliberately doesn't distinguish them) is surfaced as a plain Error, not thrown as an HTTP-shaped one - the login form just needs a message to show. */
export async function login(username: string, password: string): Promise<AuthUser> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/login`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ username, password })
	});
	if (res.status === 401) {
		throw new Error('Incorrect username or password.');
	}
	if (!res.ok) {
		throw new Error(`POST /api/auth/login failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** `POST /api/auth/logout`. Always succeeds (204) even if the session was already gone. */
export async function logout(): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/logout`, { method: 'POST' });
	if (!res.ok) {
		throw new Error(`POST /api/auth/logout failed: ${res.status} ${res.statusText}`);
	}
}

/** `GET /api/auth/me` - null (not a thrown Error) for the expected "no session yet" case, so callers doing a routine on-load check don't need a try/catch for it. */
export async function getCurrentUser(signal?: AbortSignal): Promise<AuthUser | null> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/me`, { signal });
	if (res.status === 401) {
		return null;
	}
	if (!res.ok) {
		throw new Error(`GET /api/auth/me failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** Navigates the browser to `GET /api/auth/entra/login` - a full page navigation, not a
 * fetch, since this has to leave the SPA for Microsoft's own sign-in page and come back
 * through a server-side redirect (see EntraAuthEndpoints). `returnUrl` is this window's
 * own origin, which Flare.Api validates against Cors:AllowedOrigins before ever
 * redirecting back to it. */
export function startEntraLogin(): void {
	const url = new URL(`${API_BASE_URL}/api/auth/entra/login`);
	url.searchParams.set('returnUrl', window.location.origin);
	window.location.href = url.toString();
}
