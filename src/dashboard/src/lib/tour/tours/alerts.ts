import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order. Each `target` is a `data-tour` wrapper on the page's
// toolbar or sections. A step whose target isn't on screen right now (a mode or control that
// is switched off) is skipped automatically. This tour is independent of every other page's.
export const alertsTour: TourDefinition = {
	id: 'alerts',
	path: '/alerts',
	name: m.tourAlerts_name,
	version: 1,
	steps: [
		{ title: m.tourAlerts_welcomeTitle, body: m.tourAlerts_welcomeBody },
		{ target: 'alerts-new', title: m.tourAlerts_newTitle, body: m.tourAlerts_newBody },
		{ target: 'alerts-transfer', title: m.tourAlerts_transferTitle, body: m.tourAlerts_transferBody },
		{ target: 'alerts-filters', title: m.tourAlerts_filtersTitle, body: m.tourAlerts_filtersBody },
		{ target: 'alerts-table', title: m.tourAlerts_tableTitle, body: m.tourAlerts_tableBody, side: 'top' },
		{ target: 'alerts-actions', title: m.tourAlerts_actionsTitle, body: m.tourAlerts_actionsBody, side: 'left' },
		{ title: m.tourAlerts_doneTitle, body: m.tourAlerts_doneBody }
	]
};
