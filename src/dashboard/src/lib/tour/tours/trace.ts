import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order. Each `target` is a `data-tour` wrapper on the page's
// toolbar or sections. A step whose target isn't on screen right now (a mode or control that
// is switched off) is skipped automatically. This tour is independent of every other page's.
export const traceTour: TourDefinition = {
	id: 'trace',
	pathPattern: /^\/traces\/(?!funnels$|n-plus-one$)[^/]+$/,
	name: m.tourTrace_name,
	version: 1,
	steps: [
		{ title: m.tourTrace_welcomeTitle, body: m.tourTrace_welcomeBody },
		{ target: 'trace-back', title: m.tourTrace_backTitle, body: m.tourTrace_backBody },
		{ target: 'trace-search', title: m.tourTrace_searchTitle, body: m.tourTrace_searchBody },
		{ target: 'trace-tabs', title: m.tourTrace_tabsTitle, body: m.tourTrace_tabsBody },
		{ target: 'trace-waterfall', title: m.tourTrace_waterfallTitle, body: m.tourTrace_waterfallBody, side: 'top' },
		{ target: 'trace-flame-graph', title: m.tourTrace_flameTitle, body: m.tourTrace_flameBody, side: 'top' },
		{ target: 'trace-service-map', title: m.tourTrace_serviceMapTitle, body: m.tourTrace_serviceMapBody, side: 'top' },
		{ target: 'trace-download', title: m.tourTrace_downloadTitle, body: m.tourTrace_downloadBody },
		{ target: 'trace-grafana', title: m.tourTrace_grafanaTitle, body: m.tourTrace_grafanaBody },
		{ title: m.tourTrace_doneTitle, body: m.tourTrace_doneBody }
	]
};
