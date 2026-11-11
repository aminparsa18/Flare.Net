import { replaceState } from '$app/navigation';
import { page } from '$app/state';
import { tourController } from '#lib/tour/tour.svelte.ts';
import type { TourDefinition } from '#lib/tour/types.ts';

// Gives the tour a beat to start so the toolbar and first results have rendered and the
// spotlight lands on settled layout.
const START_DELAY_MS = 800;

/**
 * Wires one page to its own tour; call it from `onMount` and return the result from the
 * cleanup (or pass it to `onDestroy`). Each page owns exactly one tour - they never chain,
 * and finishing one doesn't queue another.
 *
 * `?tour=<id>` (Settings > Explorer > Guided tours' Replay) always starts it, then drops the
 * param so a reload doesn't replay it. Otherwise it auto-starts once per browser
 * (see `maybeAutoStart`) - and only on a bare visit, since someone arriving from a shared link
 * or an alert came to look at something specific.
 */
export function startPageTour(tour: TourDefinition): () => void {
	let timer: ReturnType<typeof setTimeout> | undefined;
	if (page.url.searchParams.get('tour') === tour.id) {
		const url = new URL(page.url.href);
		url.searchParams.delete('tour');
		replaceState(url, page.state);
		timer = setTimeout(() => tourController.start(tour), START_DELAY_MS);
	} else if (page.url.searchParams.size === 0) {
		timer = setTimeout(() => tourController.maybeAutoStart(tour), START_DELAY_MS);
	}
	return () => {
		clearTimeout(timer);
		tourController.cancel(tour);
	};
}
