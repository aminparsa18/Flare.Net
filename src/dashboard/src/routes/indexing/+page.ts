import { redirect } from '@sveltejs/kit';
import { withBase } from '$lib/paths';

// Moved under Settings > Workspace; keeps old bookmarks and deep links (incl. ?query) working.
export function load({ url }) {
	redirect(307, withBase('/settings/indexing') + url.search);
}
