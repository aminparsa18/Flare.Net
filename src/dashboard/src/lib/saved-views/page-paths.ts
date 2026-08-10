// Maps a saved view's `pageType` to the dashboard route that renders it, and builds the
// shareable `?view=<id>` URL for one - the one place this mapping lives, so
// SavedViewTable.svelte (the "Open"/"Copy link" actions) and each explorer's own
// `?view=` hydration (routes/+page.svelte, routes/traces/+page.svelte,
// routes/metrics/+page.svelte) agree on it without duplicating the switch.

import type { PageType } from '$lib/saved-views-api';

/** Logs is the app's default route ('/'), not '/logs' - see AppNav.svelte's own `links` array. */
export function pageTypeBasePath(pageType: PageType): string {
	switch (pageType) {
		case 'Logs':
			return '/';
		case 'Traces':
			return '/traces';
		case 'Metrics':
			return '/metrics';
	}
}

/** A path-relative `?view=<id>` URL for `view` - prefix with `location.origin` to get a copy-pasteable absolute link. */
export function savedViewPath(view: { id: string; pageType: PageType }): string {
	return `${pageTypeBasePath(view.pageType)}?view=${view.id}`;
}
