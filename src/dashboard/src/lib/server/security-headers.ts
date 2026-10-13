// Baseline security response headers (ADR-0200). Runtime-configured (not SvelteKit's build-time kit.csp) because
// connect-src depends on PUBLIC_API_URL, which adapter-node reads per request.
//
// Env knobs (all optional):
//   FLARE_SECURITY_HEADERS=off        no headers at all (a reverse proxy owns them)
//   FLARE_CSP=off                     keep the other headers, drop Content-Security-Policy
//   FLARE_CSP_FRAME_ANCESTORS         frame-ancestors source list, default 'none'
//   FLARE_CSP_CONNECT_SRC / _IMG_SRC  extra space-separated sources appended to connect-src / img-src
//   FLARE_HSTS_MAX_AGE                seconds, default 15552000 (180 days), 0 disables; HTTPS requests only
//   FLARE_HSTS_INCLUDE_SUBDOMAINS=true

import { randomBytes } from 'node:crypto';

export interface SecurityHeadersConfig {
	enabled: boolean;
	csp: boolean;
	frameAncestors: string;
	extraConnectSrc: string;
	extraImgSrc: string;
	hstsMaxAge: number;
	hstsIncludeSubDomains: boolean;
}

export function readConfig(env: Record<string, string | undefined>): SecurityHeadersConfig {
	const off = (v: string | undefined) => (v ?? '').trim().toLowerCase() === 'off';
	const maxAge = Number.parseInt(env.FLARE_HSTS_MAX_AGE ?? '', 10);
	return {
		enabled: !off(env.FLARE_SECURITY_HEADERS),
		csp: !off(env.FLARE_CSP),
		frameAncestors: env.FLARE_CSP_FRAME_ANCESTORS?.trim() || "'none'",
		extraConnectSrc: env.FLARE_CSP_CONNECT_SRC?.trim() ?? '',
		extraImgSrc: env.FLARE_CSP_IMG_SRC?.trim() ?? '',
		hstsMaxAge: Number.isFinite(maxAge) && maxAge >= 0 ? maxAge : 15552000,
		hstsIncludeSubDomains: (env.FLARE_HSTS_INCLUDE_SUBDOMAINS ?? '').trim().toLowerCase() === 'true'
	};
}

export function newNonce(): string {
	return randomBytes(16).toString('base64');
}

/** Origin of the API for connect-src/form-action, or null when it is relative/unparseable (then 'self' covers it). */
function apiOrigin(apiUrl: string | undefined): string | null {
	try {
		return apiUrl ? new URL(apiUrl).origin : null;
	} catch {
		return null;
	}
}

function wsOrigin(origin: string): string {
	return origin.replace(/^http/, 'ws');
}

export function buildCsp(
	cfg: SecurityHeadersConfig,
	nonce: string,
	requestOrigin: string,
	apiUrl: string | undefined,
	remoteImages = false
): string {
	const api = apiOrigin(apiUrl);
	// 'self' does not reliably cover ws(s) on the same origin in every browser, so the live-tail socket is spelled out.
	const connect = ["'self'", wsOrigin(requestOrigin), ...(api ? [api, wsOrigin(api)] : []), cfg.extraConnectSrc];
	// Only the public status page loads a remote image (its admin-set logo). Everywhere else images are same-origin or
	// data:/blob:, so telemetry-derived <img> sources cannot reach an arbitrary host and leak the viewer's IP.
	const img = ["'self'", 'data:', 'blob:', ...(remoteImages ? ['https:'] : []), cfg.extraImgSrc];
	return [
		"default-src 'self'",
		// Every inline <script> gets the nonce via transformPageChunk (see addNonce); nothing else may execute.
		`script-src 'self' 'nonce-${nonce}'`,
		// Inline style attributes (app.html's display:contents wrapper, component libraries) need 'unsafe-inline'.
		"style-src 'self' 'unsafe-inline'",
		`img-src ${img.filter(Boolean).join(' ')}`,
		"font-src 'self' data:",
		`connect-src ${connect.filter(Boolean).join(' ')}`,
		"worker-src 'self' blob:",
		"object-src 'none'",
		"base-uri 'self'",
		`form-action 'self'${api ? ` ${api}` : ''}`,
		`frame-ancestors ${cfg.frameAncestors}`
	].join('; ');
}

/** Marks every `<script` opening tag in an SSR chunk with the request nonce. */
export function addNonce(html: string, nonce: string): string {
	return html.replaceAll('<script', `<script nonce="${nonce}"`);
}

export function securityHeaders(
	cfg: SecurityHeadersConfig,
	opts: { isHttps: boolean; nonce: string; requestOrigin: string; apiUrl: string | undefined; remoteImages?: boolean }
): [string, string][] {
	const headers: [string, string][] = [
		['X-Content-Type-Options', 'nosniff'],
		['Referrer-Policy', 'same-origin']
	];
	if (cfg.csp) headers.push(['Content-Security-Policy', buildCsp(cfg, opts.nonce, opts.requestOrigin, opts.apiUrl, opts.remoteImages)]);
	if (opts.isHttps && cfg.hstsMaxAge > 0) {
		headers.push([
			'Strict-Transport-Security',
			`max-age=${cfg.hstsMaxAge}${cfg.hstsIncludeSubDomains ? '; includeSubDomains' : ''}`
		]);
	}
	return headers;
}
