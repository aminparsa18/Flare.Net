import { withBase } from '#lib/paths.ts';
import type { TourDefinition } from '#lib/tour/types.ts';
import { logsTour } from './logs.ts';

// Every page tour. Adding one = tag its sections with data-tour, write its steps file, add it
// here, and have its page handle `?tour=<id>` and call maybeAutoStart (see routes/+page.svelte). The
// user menu, command palette and Settings > Explorer > Guided tours all read this list.
export const ALL_TOURS: readonly TourDefinition[] = [logsTour];

export function tourForPath(path: string): TourDefinition | null {
	return ALL_TOURS.find((t) => t.path === path) ?? null;
}

/**
 * Link that opens a tour's page and starts it there (Settings' Replay button). The query param
 * also keeps a bare "/" from redirecting to a home dashboard or a different landing page.
 */
export function tourReplayHref(tour: TourDefinition): string {
	return withBase(`${tour.path}?tour=${encodeURIComponent(tour.id)}`);
}
