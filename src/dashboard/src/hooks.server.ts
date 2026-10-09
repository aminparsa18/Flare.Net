import { error, redirect, type Handle } from '@sveltejs/kit';
import { allowedOnStatusDomain, slugForHost } from '$lib/server/status-domains';
import { paraglideMiddleware } from '$lib/paraglide/server';

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

	return paraglideMiddleware(event.request, ({ request, locale }) => {
		event.request = request;
		return resolve(event, {
			// replaceAll, not replace: a plain .replace() only swaps the first match in the
			// whole document, and app.html's own explanatory comment about this placeholder
			// was matching before the real <html lang> attribute did - see its comment.
			transformPageChunk: ({ html }) => html.replaceAll('%paraglide.lang%', locale)
		});
	});
};
