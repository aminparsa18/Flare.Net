import { error, redirect, type Handle } from '@sveltejs/kit';
import { allowedOnStatusDomain, slugForHost } from '$lib/server/status-domains';
import { paraglideMiddleware } from '$lib/paraglide/server';
import { dev } from '$app/environment';
import { env } from '$env/dynamic/public';
import { env as privateEnv } from '$env/dynamic/private';
import { addNonce, newNonce, readConfig, securityHeaders } from '$lib/server/security-headers';

// This app SSRs its shell (adapter-node, no ssr=false anywhere - see vite.config.ts's
// comment), so the locale has to be resolved on the server, not just in the client-side
// language switcher (AppNav's NavUserMenu) - otherwise a direct hit on /login (bookmark,
// browser back) would render in the wrong locale until hydration catches up.
// paraglideMiddleware runs the configured strategy chain (cookie -> preferredLanguage -> baseLocale, see
// vite.config.ts/package.json's codegen script) and makes the result available to every
// m.*() call made while handling this request via AsyncLocalStorage.
export const handle: Handle = async ({ event, resolve }) => {
	// A host that belongs to a status page (ADR-0165) serves only that page: / goes to it and everything else, the
	// admin app included, is a 404, so pointing a public hostname at the dashboard does not expose the dashboard.
	const slug = await slugForHost(event.url.hostname);
	if (slug) {
		if (event.url.pathname === '/') redirect(302, `/status/${slug}`);
		if (!allowedOnStatusDomain(event.url.pathname, slug)) error(404, 'Not found');
	}

	// Baseline security headers (ADR-0200). Off in `vite dev`, whose HMR client is an inline script.
	const security = readConfig(privateEnv);
	const withHeaders = security.enabled && !dev;
	const nonce = newNonce();

	const response = await paraglideMiddleware(event.request, ({ request, locale }) => {
		event.request = request;
		return resolve(event, {
			// replaceAll, not replace: a plain .replace() only swaps the first match in the
			// whole document, and app.html's own explanatory comment about this placeholder
			// was matching before the real <html lang> attribute did - see its comment.
			transformPageChunk: ({ html }) => {
				const localized = html.replaceAll('%paraglide.lang%', locale);
				return withHeaders && security.csp ? addNonce(localized, nonce) : localized;
			}
		});
	});

	if (withHeaders) {
		// Not event.url.protocol: adapter-node assumes https unless PROTOCOL_HEADER is set, so it says nothing
		// about the client's real scheme. Only the proxy's X-Forwarded-Proto does; absent means plain HTTP.
		const forwarded = event.request.headers.get('x-forwarded-proto')?.split(',')[0].trim().toLowerCase();
		const proto = forwarded === 'https' ? 'https' : 'http';
		for (const [name, value] of securityHeaders(security, {
			isHttps: proto === 'https',
			nonce,
			requestOrigin: `${proto}://${event.url.host}`,
			apiUrl: env.PUBLIC_API_URL,
			remoteImages: event.url.pathname.startsWith('/status/')
		})) {
			// Only on responses that do not already carry the header (an endpoint may have chosen its own).
			if (!response.headers.has(name)) response.headers.set(name, value);
		}
	}

	return response;
};
