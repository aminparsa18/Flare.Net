import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order. Each `target` is a `data-tour` wrapper on the page's
// toolbar or sections. A step whose target isn't on screen right now (a mode or control that
// is switched off) is skipped automatically. This tour is independent of every other page's.
export const funnelsTour: TourDefinition = {
	id: 'funnels',
	path: '/traces/funnels',
	name: m.tourFunnels_name,
	version: 1,
	steps: [
		{ title: m.tourFunnels_welcomeTitle, body: m.tourFunnels_welcomeBody },
		{ target: 'funnels-tabs', title: m.tourFunnels_tabsTitle, body: m.tourFunnels_tabsBody },
		{ target: 'funnels-intro', title: m.tourFunnels_introTitle, body: m.tourFunnels_introBody },
		{ target: 'funnels-steps', title: m.tourFunnels_stepsTitle, body: m.tourFunnels_stepsBody },
		{ target: 'funnels-window', title: m.tourFunnels_windowTitle, body: m.tourFunnels_windowBody },
		{ target: 'funnels-saved', title: m.tourFunnels_savedTitle, body: m.tourFunnels_savedBody },
		{ target: 'funnels-run', title: m.tourFunnels_runTitle, body: m.tourFunnels_runBody },
		{ target: 'funnels-results', title: m.tourFunnels_resultsTitle, body: m.tourFunnels_resultsBody, side: 'top' },
		{ title: m.tourFunnels_doneTitle, body: m.tourFunnels_doneBody }
	]
};
