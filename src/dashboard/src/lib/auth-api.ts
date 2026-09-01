// Client for Flare.Api's auth endpoints (src/Flare.Api/Endpoints/AuthEndpoints.cs).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack: every function here sends/receives `application/x-memorypack` (see
// `$lib/api.ts`'s header comment) and decodes through the generated classes under
// `$lib/generated/memorypack/` (regenerated from `src/Flare.Api/Model/AuthModels.cs` by
// `dotnet build` - see package.json's `predev`/`prebuild`/`precheck`), not `res.json()`.
// `UserRole` on the wire is MemoryPack's raw numeric ordinal, not the string name JSON's
// `UseStringEnumConverter` used to send - `$lib/memorypack/enums.ts`'s `userRoleToString`/
// `userRoleFromString` convert at this module's boundary so every exported interface below
// (and every consumer outside `lib/`) is unchanged. `authProvider` is a plain C# `string`
// already (never an enum server-side), so it round-trips with no conversion.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import { AuthUserDto } from '$lib/generated/memorypack/AuthUserDto.js';
import { LoginRequest } from '$lib/generated/memorypack/LoginRequest.js';
import { LogoutResponse as GeneratedLogoutResponse } from '$lib/generated/memorypack/LogoutResponse.js';
import { BootstrapStatusResponse as GeneratedBootstrapStatusResponse } from '$lib/generated/memorypack/BootstrapStatusResponse.js';
import { userRoleToString, type UserRoleName } from '$lib/memorypack/enums';

export type UserRole = UserRoleName;

export type AuthProvider = 'Local' | 'Entra' | 'ActiveDirectory' | 'Oidc' | 'ReverseProxy';

export interface AuthUser {
	id: string;
	username: string;
	role: UserRole;
	authProvider: AuthProvider;
}

function toAuthUser(dto: AuthUserDto): AuthUser {
	return {
		id: dto.id,
		username: dto.username ?? '',
		role: userRoleToString(dto.role),
		authProvider: dto.authProvider as AuthProvider
	};
}

async function decodeAuthUser(res: Response): Promise<AuthUser> {
	const dto = AuthUserDto.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding AuthUserDto.');
	}
	return toAuthUser(dto);
}

export interface BootstrapStatusResponse {
	/** The global switch (opt-in auth - see docs/auth.md). False means every endpoint is
	 * open to anyone - `+layout.svelte`'s route guard skips the whole login/setup
	 * redirect dance entirely when this is false. */
	authEnabled: boolean;
	needsBootstrap: boolean;
	localEnabled: boolean;
	/** Whether Entra ID is configured+enabled - gates the "Sign in with Microsoft"
	 * button on /login (see EntraAuthEndpoints.HandleLoginAsync, which itself 404s when
	 * this is false - the button check is a UX nicety on top of that, not the actual
	 * enforcement). */
	entraEnabled: boolean;
	/** Whether Active Directory is configured+enabled - gates the "Active Directory"
	 * sign-in option on /login the same way `entraEnabled` gates the Microsoft button
	 * (see LdapAuthEndpoints.HandleLoginAsync's own disabled-gate 404). */
	ldapEnabled: boolean;
	/** Whether generic OpenID Connect is configured+enabled - gates the "Sign in with
	 * {oidcDisplayName}" button on /login the same way `entraEnabled` gates the
	 * Microsoft button (see OidcAuthEndpoints.HandleLoginAsync's own disabled-gate 404). */
	oidcEnabled: boolean;
	/** The dashboard-configured button label (e.g. "Okta") - unlike Entra's fixed
	 * "Microsoft" wording, a generic provider has no built-in brand to hardcode. Null
	 * when `oidcEnabled` is false or no display name was ever set. */
	oidcDisplayName: string | null;
	/** Whether reverse-proxy (trusted header) auth is configured+enabled - unlike every
	 * other method, this has no button on /login: when true, the page calls
	 * `loginViaProxy()` automatically (see ProxyAuthLoginEndpoints' own disabled-gate
	 * 404), since there's no user action to trigger. */
	proxyAuthEnabled: boolean;
}

function toBootstrapStatusResponse(dto: GeneratedBootstrapStatusResponse): BootstrapStatusResponse {
	return {
		authEnabled: dto.authEnabled,
		needsBootstrap: dto.needsBootstrap,
		localEnabled: dto.localEnabled,
		entraEnabled: dto.entraEnabled,
		ldapEnabled: dto.ldapEnabled,
		oidcEnabled: dto.oidcEnabled,
		oidcDisplayName: dto.oidcDisplayName,
		proxyAuthEnabled: dto.proxyAuthEnabled
	};
}

/** `GET /api/auth/bootstrap/status` - decides whether `+layout.svelte`'s route guard sends an unauthenticated visitor to `/setup` or `/login`. */
export async function getBootstrapStatus(signal?: AbortSignal): Promise<BootstrapStatusResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/bootstrap/status`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/auth/bootstrap/status failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedBootstrapStatusResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding BootstrapStatusResponse.');
	}
	return toBootstrapStatusResponse(dto);
}

/** `POST /api/auth/bootstrap` - first-run only, creates the first Admin and signs them in. 409s (surfaced as a plain Error) if an admin already exists. */
export async function bootstrap(username: string, password: string): Promise<AuthUser> {
	const request = new LoginRequest();
	request.username = username;
	request.password = password;
	const res = await apiFetch(`${API_BASE_URL}/api/auth/bootstrap`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(LoginRequest.serialize(request))
	});
	if (res.status === 409) {
		throw new Error('An admin user already exists.');
	}
	if (!res.ok) {
		throw new Error(`POST /api/auth/bootstrap failed: ${res.status} ${res.statusText}`);
	}
	return decodeAuthUser(res);
}

/** `POST /api/auth/login`. A 401 (wrong username/password, or an unknown username - AuthEndpoints deliberately doesn't distinguish them) is surfaced as a plain Error, not thrown as an HTTP-shaped one - the login form just needs a message to show. */
export async function login(username: string, password: string): Promise<AuthUser> {
	const request = new LoginRequest();
	request.username = username;
	request.password = password;
	const res = await apiFetch(`${API_BASE_URL}/api/auth/login`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(LoginRequest.serialize(request))
	});
	if (res.status === 401) {
		throw new Error('Incorrect username or password.');
	}
	if (!res.ok) {
		throw new Error(`POST /api/auth/login failed: ${res.status} ${res.statusText}`);
	}
	return decodeAuthUser(res);
}

/** `POST /api/auth/ldap/login` - same shape/semantics as `login` above (including the
 * generic 401 message, mirroring LdapAuthEndpoints' own anti-enumeration stance), just a
 * different endpoint and credential set (the AD account, not the local one). A 502
 * (LdapAuthEndpoints' distinct "could not reach the LDAP server" response, a Flare-side
 * config problem rather than a wrong password) gets its own message so an Admin
 * debugging a broken Active Directory config isn't misled into thinking every login is
 * a bad password. */
export async function loginLdap(username: string, password: string): Promise<AuthUser> {
	const request = new LoginRequest();
	request.username = username;
	request.password = password;
	const res = await apiFetch(`${API_BASE_URL}/api/auth/ldap/login`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(LoginRequest.serialize(request))
	});
	if (res.status === 401) {
		throw new Error('Incorrect username or password.');
	}
	if (res.status === 502) {
		throw new Error('Could not reach the Active Directory server. Contact an Admin.');
	}
	if (!res.ok) {
		throw new Error(`POST /api/auth/ldap/login failed: ${res.status} ${res.statusText}`);
	}
	return decodeAuthUser(res);
}

export interface LogoutResponse {
	/** Non-null only for a ReverseProxy-provisioned account with a configured
	 * `ProxyAuthSettings.logoutRedirectUrl` - see docs/auth.md's "Reverse proxy (trusted
	 * header)" section, "Known limitations". Null for every other account (or a
	 * reverse-proxy account with none configured), same as before this field existed. */
	redirectUrl: string | null;
}

/** `POST /api/auth/logout`. Always succeeds even if the session was already gone. */
export async function logout(): Promise<LogoutResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/logout`, { method: 'POST', headers: memoryPackAcceptHeaders() });
	if (!res.ok) {
		throw new Error(`POST /api/auth/logout failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedLogoutResponse.deserialize(await res.arrayBuffer());
	return { redirectUrl: dto?.redirectUrl ?? null };
}

/** `GET /api/auth/me` - null (not a thrown Error) for the expected "no session yet" case, so callers doing a routine on-load check don't need a try/catch for it. */
export async function getCurrentUser(signal?: AbortSignal): Promise<AuthUser | null> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/me`, { headers: memoryPackAcceptHeaders(), signal });
	if (res.status === 401) {
		return null;
	}
	if (!res.ok) {
		throw new Error(`GET /api/auth/me failed: ${res.status} ${res.statusText}`);
	}
	return decodeAuthUser(res);
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

/** Navigates the browser to `GET /api/auth/oidc/login` - same full-page-navigation shape
 * as {@link startEntraLogin}, just the generic OIDC scheme (see OidcAuthEndpoints). */
export function startOidcLogin(): void {
	const url = new URL(`${API_BASE_URL}/api/auth/oidc/login`);
	url.searchParams.set('returnUrl', window.location.origin);
	window.location.href = url.toString();
}

/** `POST /api/auth/proxy/login` - unlike {@link startEntraLogin}/{@link startOidcLogin},
 * a same-origin `fetch`, not a full-page navigation: identity is already established by
 * the time this request reaches Flare.Api (the reverse proxy set it on this very
 * request), there's no external provider to redirect to. Called automatically by
 * `/login` when `proxyAuthEnabled` is true - see ProxyAuthLoginEndpoints for the
 * possible failure responses (404 disabled, 403 untrusted network, 401 header missing or
 * account disabled). */
export async function loginViaProxy(): Promise<AuthUser> {
	const res = await apiFetch(`${API_BASE_URL}/api/auth/proxy/login`, { method: 'POST', headers: memoryPackAcceptHeaders() });
	if (!res.ok) {
		throw new Error(`POST /api/auth/proxy/login failed: ${res.status} ${res.statusText}`);
	}
	return decodeAuthUser(res);
}
