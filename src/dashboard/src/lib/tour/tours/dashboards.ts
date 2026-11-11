import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order. Each `target` is a `data-tour` wrapper on the page's
// toolbar or sections. A step whose target isn't on screen right now (a mode or control that
// is switched off) is skipped automatically. This tour is independent of every other page's.
export const dashboardsTour: TourDefinition = {
	id: 'dashboards',
	path: '/dashboards',
	name: m.tourDashboards_name,
	version: 1,
	steps: [
		{ title: m.tourDashboards_welcomeTitle, body: m.tourDashboards_welcomeBody },
		{ target: 'dashboards-template', title: m.tourDashboards_templateTitle, body: m.tourDashboards_templateBody },
		{ target: 'dashboards-import', title: m.tourDashboards_importTitle, body: m.tourDashboards_importBody },
		{ target: 'dashboards-new', title: m.tourDashboards_newTitle, body: m.tourDashboards_newBody },
		{ target: 'dashboards-filters', title: m.tourDashboards_filtersTitle, body: m.tourDashboards_filtersBody },
		{ target: 'dashboards-table', title: m.tourDashboards_tableTitle, body: m.tourDashboards_tableBody, side: 'top' },
		{ target: 'dashboards-actions', title: m.tourDashboards_actionsTitle, body: m.tourDashboards_actionsBody, side: 'left' },
		{ title: m.tourDashboards_doneTitle, body: m.tourDashboards_doneBody }
	]
};
