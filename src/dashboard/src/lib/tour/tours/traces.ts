import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order. Each `target` is a `data-tour` wrapper on the page's
// toolbar or sections. A step whose target isn't on screen right now (a mode or control that
// is switched off) is skipped automatically. This tour is independent of every other page's.
export const tracesTour: TourDefinition = {
	id: 'traces',
	path: '/traces',
	name: m.tourTraces_name,
	version: 1,
	steps: [
		{ title: m.tourTraces_welcomeTitle, body: m.tourTraces_welcomeBody },
		{ target: 'traces-tabs', title: m.tourTraces_tabsTitle, body: m.tourTraces_tabsBody },
		{ target: 'traces-time', title: m.tourTraces_timeTitle, body: m.tourTraces_timeBody },
		{ target: 'traces-services', title: m.tourTraces_servicesTitle, body: m.tourTraces_servicesBody },
		{ target: 'traces-go-to', title: m.tourTraces_goToTitle, body: m.tourTraces_goToBody },
		{ target: 'traces-structure', title: m.tourTraces_structureTitle, body: m.tourTraces_structureBody },
		{ target: 'traces-n-plus-one', title: m.tourTraces_nPlusOneTitle, body: m.tourTraces_nPlusOneBody },
		{ target: 'traces-entry-spans', title: m.tourTraces_entrySpansTitle, body: m.tourTraces_entrySpansBody },
		{ target: 'traces-auto-refresh', title: m.tourTraces_autoRefreshTitle, body: m.tourTraces_autoRefreshBody },
		{ target: 'traces-export', title: m.tourTraces_exportTitle, body: m.tourTraces_exportBody },
		{ target: 'traces-saved', title: m.tourTraces_savedTitle, body: m.tourTraces_savedBody },
		{ target: 'traces-facets', title: m.tourTraces_facetsTitle, body: m.tourTraces_facetsBody, side: 'right' },
		{ target: 'traces-attribute-filters', title: m.tourTraces_attributeFiltersTitle, body: m.tourTraces_attributeFiltersBody },
		{ target: 'traces-list', title: m.tourTraces_listTitle, body: m.tourTraces_listBody, side: 'top' },
		{ title: m.tourTraces_doneTitle, body: m.tourTraces_doneBody }
	]
};
