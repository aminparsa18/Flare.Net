// Client for Flare.Api's Admin-only reverse-proxy (trusted header) settings API
// (src/Flare.Api/Endpoints/ProxyAuthSettingsEndpoints.cs).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape, and
// `$lib/memorypack/enums.ts` for why `defaultRole` converts through `userRoleToString`/
// `userRoleFromString` instead of being passed through as MemoryPack's raw numeric ordinal.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import type { UserRole } from './auth-api';
import { userRoleFromString, userRoleToString } from '$lib/memorypack/enums';
import { ProxyAuthSettingsDto } from '$lib/generated/memorypack/ProxyAuthSettingsDto.js';
import { SaveProxyAuthSettingsRequest as GeneratedSaveProxyAuthSettingsRequest } from '$lib/generated/memorypack/SaveProxyAuthSettingsRequest.js';

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
	/** Optional. When set, `/api/auth/logout` sends the browser here (instead of back to
	 * /login) after clearing the local session, for ReverseProxy-provisioned accounts -
	 * see docs/auth.md's "Known limitations": Flare can't propagate logout to the
	 * proxy/IdP automatically, so this is a manual escape hatch pointing at your proxy's
	 * own sign-out URL. */
	logoutRedirectUrl: string | null;
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
	logoutRedirectUrl: string | null;
}

function toProxyAuthSettings(dto: ProxyAuthSettingsDto): ProxyAuthSettings {
	return {
		enabled: dto.enabled,
		headerName: dto.headerName ?? '',
		trustedProxyCidrs: dto.trustedProxyCidrs ?? '',
		groupsHeaderName: dto.groupsHeaderName,
		adminGroup: dto.adminGroup,
		memberGroup: dto.memberGroup,
		viewerGroup: dto.viewerGroup,
		defaultRole: userRoleToString(dto.defaultRole),
		logoutRedirectUrl: dto.logoutRedirectUrl
	};
}

async function decodeProxyAuthSettings(res: Response): Promise<ProxyAuthSettings> {
	const dto = ProxyAuthSettingsDto.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding ProxyAuthSettingsDto.');
	}
	return toProxyAuthSettings(dto);
}

/** `GET /api/settings/proxyauth`. */
export async function getProxyAuthSettings(signal?: AbortSignal): Promise<ProxyAuthSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/proxyauth`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/settings/proxyauth failed: ${res.status} ${res.statusText}`);
	}
	return decodeProxyAuthSettings(res);
}

/** `PUT /api/settings/proxyauth`. 400s if `enabled: true` has a blank header name or no CIDR entry that actually parses, if `trustedProxyCidrs` includes the 0.0.0.0/0 or ::/0 catch-all (rejected unconditionally, not just while enabling), or if `logoutRedirectUrl` is set but isn't a valid absolute URL - surfaced as a plain Error. */
export async function saveProxyAuthSettings(request: SaveProxyAuthSettingsRequest): Promise<ProxyAuthSettings> {
	const dto = new GeneratedSaveProxyAuthSettingsRequest();
	dto.enabled = request.enabled;
	dto.headerName = request.headerName;
	dto.trustedProxyCidrs = request.trustedProxyCidrs;
	dto.groupsHeaderName = request.groupsHeaderName;
	dto.adminGroup = request.adminGroup;
	dto.memberGroup = request.memberGroup;
	dto.viewerGroup = request.viewerGroup;
	dto.defaultRole = userRoleFromString(request.defaultRole);
	dto.logoutRedirectUrl = request.logoutRedirectUrl;
	const res = await apiFetch(`${API_BASE_URL}/api/settings/proxyauth`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSaveProxyAuthSettingsRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/settings/proxyauth failed: ${res.status} ${res.statusText}`);
	}
	return decodeProxyAuthSettings(res);
}
