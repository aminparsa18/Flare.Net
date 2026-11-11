import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order. Each `target` is a `data-tour` wrapper on the page's
// toolbar or sections. A step whose target isn't on screen right now (a mode or control that
// is switched off) is skipped automatically. This tour is independent of every other page's.
export const dashboardTour: TourDefinition = {
	id: 'dashboard',
	pathPattern: /^\/dashboards\/[^/]+$/,
	name: m.tourDashboard_name,
	version: 1,
	steps: [
		{ title: m.tourDashboard_welcomeTitle, body: m.tourDashboard_welcomeBody },
		{ target: 'dashboard-home', title: m.tourDashboard_homeTitle, body: m.tourDashboard_homeBody },
		{ target: 'dashboard-refresh', title: m.tourDashboard_refreshTitle, body: m.tourDashboard_refreshBody },
		{ target: 'dashboard-time-range', title: m.tourDashboard_timeRangeTitle, body: m.tourDashboard_timeRangeBody },
		{ target: 'dashboard-variables', title: m.tourDashboard_variablesTitle, body: m.tourDashboard_variablesBody },
		{ target: 'dashboard-schedules', title: m.tourDashboard_schedulesTitle, body: m.tourDashboard_schedulesBody },
		{ target: 'dashboard-edit', title: m.tourDashboard_editTitle, body: m.tourDashboard_editBody },
		{ target: 'dashboard-manage-variables', title: m.tourDashboard_manageVariablesTitle, body: m.tourDashboard_manageVariablesBody },
		{ target: 'dashboard-add-row', title: m.tourDashboard_addRowTitle, body: m.tourDashboard_addRowBody },
		{ target: 'dashboard-add-panel', title: m.tourDashboard_addPanelTitle, body: m.tourDashboard_addPanelBody },
		{ target: 'dashboard-fullscreen', title: m.tourDashboard_fullscreenTitle, body: m.tourDashboard_fullscreenBody },
		{ target: 'dashboard-panels', title: m.tourDashboard_panelsTitle, body: m.tourDashboard_panelsBody, side: 'top' },
		{ title: m.tourDashboard_doneTitle, body: m.tourDashboard_doneBody }
	]
};
