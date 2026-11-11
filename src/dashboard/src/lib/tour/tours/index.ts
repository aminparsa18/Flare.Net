import { withBase } from '#lib/paths.ts';
import type { TourDefinition } from '#lib/tour/types.ts';
import { alertsTour } from './alerts.ts';
import { dashboardsTour } from './dashboards.ts';
import { errorsTour } from './errors.ts';
import { logsTour } from './logs.ts';
import { metricsTour } from './metrics.ts';
import { tracesTour } from './traces.ts';
import { traceTour } from './trace.ts';
import { funnelsTour } from './funnels.ts';
import { catalogTour } from './catalog.ts';
import { dashboardTour } from './dashboard.ts';

// Every page tour. Each is independent: its own id, path, seen-flag and steps, and finishing one
// never leads into another. Adding one = tag its sections with data-tour, write its steps file,
// add it here, and call startPageTour(tour) from the page's onMount (see #lib/tour/page.ts). The
// user menu, command palette and Settings > Explorer > Guided tours all read this list.
export const ALL_TOURS: readonly TourDefinition[] = [
	logsTour,
	tracesTour,
	traceTour,
	funnelsTour,
	metricsTour,
	catalogTour,
	dashboardsTour,
	dashboardTour,
	errorsTour,
	alertsTour
];

export function tourForPath(path: string): TourDefinition | null {
	// A fixed path wins over a pattern, so /traces/funnels never falls through to the trace-detail tour.
	return ALL_TOURS.find((t) => t.path === path) ?? ALL_TOURS.find((t) => t.pathPattern?.test(path)) ?? null;
}

/**
 * Link that opens a tour's page and starts it there (Settings' Replay button); null for a dynamic-route tour. The query param
 * also keeps a bare "/" from redirecting to a home dashboard or a different landing page.
 */
export function tourReplayHref(tour: TourDefinition): string | null {
	return tour.path ? withBase(`${tour.path}?tour=${encodeURIComponent(tour.id)}`) : null;
}
