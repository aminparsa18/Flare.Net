import type { Handle } from '@sveltejs/kit';
import { paraglideMiddleware } from '$lib/paraglide/server';

// This app SSRs its shell (adapter-node, no ssr=false anywhere - see vite.config.ts's
// comment), so the locale has to be resolved on the server, not just in the client-side
// language switcher (AppNav's NavUserMenu) - otherwise a direct hit on /login (bookmark,
// browser back) would render in the wrong locale until hydration catches up.
// paraglideMiddleware runs the configured strategy chain (cookie -> preferredLanguage -> baseLocale, see
// vite.config.ts/package.json's codegen script) and makes the result available to every
// m.*() call made while handling this request via AsyncLocalStorage.
export const handle: Handle = ({ event, resolve }) =>
	paraglideMiddleware(event.request, ({ request, locale }) => {
		event.request = request;
		return resolve(event, {
			// replaceAll, not replace: a plain .replace() only swaps the first match in the
			// whole document, and app.html's own explanatory comment about this placeholder
			// was matching before the real <html lang> attribute did - see its comment.
			transformPageChunk: ({ html }) => html.replaceAll('%paraglide.lang%', locale)
		});
	});
