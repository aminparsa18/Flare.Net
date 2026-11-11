import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order. Each `target` is a `data-tour` wrapper on the page's
// toolbar or sections. A step whose target isn't on screen right now (a mode or control that
// is switched off) is skipped automatically. This tour is independent of every other page's.
export const catalogTour: TourDefinition = {
	id: 'catalog',
	path: '/metrics/catalog',
	name: m.tourCatalog_name,
	version: 1,
	steps: [
		{ title: m.tourCatalog_welcomeTitle, body: m.tourCatalog_welcomeBody },
		{ target: 'catalog-search', title: m.tourCatalog_searchTitle, body: m.tourCatalog_searchBody },
		{ target: 'catalog-refresh', title: m.tourCatalog_refreshTitle, body: m.tourCatalog_refreshBody },
		{ target: 'catalog-window', title: m.tourCatalog_windowTitle, body: m.tourCatalog_windowBody },
		{ target: 'catalog-table', title: m.tourCatalog_tableTitle, body: m.tourCatalog_tableBody, side: 'top' },
		{ title: m.tourCatalog_doneTitle, body: m.tourCatalog_doneBody }
	]
};
