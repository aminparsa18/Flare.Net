// Shared onMount hydration for the `?view=<id>` shareable-link mechanism - the entire
// "shareable" half of the saved-views feature. Deliberately narrow: a one-shot,
// mount-time-only read of a single query param, not the generalized "every filter change
// reflects into the URL" work Planning.md's v5 section flagged as separate, larger, and
// still not started - see saved-views-api.ts's module comment for the full framing.

import { getSavedView, type PageType, type SavedView } from '$lib/saved-views-api';

/**
 * Reads a `?view=<id>` query param off `url` and resolves the saved view it names -
 * `null` if there's no such param, the view doesn't exist, the fetch failed, or it
 * belongs to a different page than `expectedPageType` (e.g. a Logs view's link opened
 * while landing on /traces). Every caller falls back to its own hardcoded defaults on
 * `null`, same as if no `view` param had been present at all.
 */
export async function resolveRequestedSavedView(url: URL, expectedPageType: PageType): Promise<SavedView | null> {
	const viewId = url.searchParams.get('view');
	if (!viewId) return null;

	try {
		const view = await getSavedView(viewId);
		if (view.pageType !== expectedPageType) {
			console.error(`Saved view ${viewId} is a ${view.pageType} view, not ${expectedPageType} - ignoring.`);
			return null;
		}
		return view;
	} catch (err) {
		console.error(`Failed to load saved view ${viewId}:`, err);
		return null;
	}
}
