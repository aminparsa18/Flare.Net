// Sub-path support (docs/how-to/deploy/serve-under-a-sub-path.md): when Flare is served at
// https://example.com/flare/, SvelteKit's `paths.base` is "/flare" and every root-relative
// URL the app builds by hand (href="/traces", goto('/login'), deep-link builders) has to
// carry it - SvelteKit only prefixes its own generated asset URLs, never a literal "/x".
// Everything that emits or reads an in-app path goes through these two helpers; with no
// base path configured both are the identity function.

import { resolve } from '$app/paths';

// SvelteKit 3 no longer exports `base`; resolving the empty pathname yields `${base}/`. Read lazily because on
// the server resolve() needs an active request when `paths.relative` is on (it is off, see vite.config.ts).
function basePath(): string {
	return (resolve as (pathname: string) => string)('').replace(/\/$/, '');
}

/** Prefixes an in-app, root-relative path ("/traces?x=1") with the configured base path. */
export function withBase(path: string): string {
	return path.startsWith('/') ? `${basePath()}${path}` : path;
}

/** The inverse for `page.url.pathname` - route-relative again, so it compares against "/traces". */
export function stripBase(pathname: string): string {
	const base = basePath();
	if (base && (pathname === base || pathname.startsWith(`${base}/`))) {
		return pathname.slice(base.length) || '/';
	}
	return pathname;
}

/** This window's origin plus the base path - what an SSO round trip should return to. */
export function appOrigin(): string {
	return `${window.location.origin}${basePath()}`;
}
