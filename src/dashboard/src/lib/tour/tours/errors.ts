import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order. Each `target` is a `data-tour` wrapper on the page's
// toolbar or sections. A step whose target isn't on screen right now (a mode or control that
// is switched off) is skipped automatically. This tour is independent of every other page's.
export const errorsTour: TourDefinition = {
	id: 'errors',
	path: '/errors',
	name: m.tourErrors_name,
	version: 1,
	steps: [
		{ title: m.tourErrors_welcomeTitle, body: m.tourErrors_welcomeBody },
		{ target: 'errors-time', title: m.tourErrors_timeTitle, body: m.tourErrors_timeBody },
		{ target: 'errors-services', title: m.tourErrors_servicesTitle, body: m.tourErrors_servicesBody },
		{ target: 'errors-status', title: m.tourErrors_statusTitle, body: m.tourErrors_statusBody },
		{ target: 'errors-mine', title: m.tourErrors_mineTitle, body: m.tourErrors_mineBody },
		{ target: 'errors-grouping', title: m.tourErrors_groupingTitle, body: m.tourErrors_groupingBody },
		{ target: 'errors-ownership', title: m.tourErrors_ownershipTitle, body: m.tourErrors_ownershipBody },
		{ target: 'errors-refresh', title: m.tourErrors_refreshTitle, body: m.tourErrors_refreshBody },
		{ target: 'errors-facets', title: m.tourErrors_facetsTitle, body: m.tourErrors_facetsBody, side: 'right' },
		{ target: 'errors-resource-filters', title: m.tourErrors_resourceFiltersTitle, body: m.tourErrors_resourceFiltersBody },
		{ target: 'errors-table', title: m.tourErrors_tableTitle, body: m.tourErrors_tableBody, side: 'top' },
		{ title: m.tourErrors_doneTitle, body: m.tourErrors_doneBody }
	]
};
