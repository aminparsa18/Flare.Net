import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order. Each `target` is a `data-tour` wrapper on the page's
// toolbar or sections. A step whose target isn't on screen right now (a mode or control that
// is switched off) is skipped automatically. This tour is independent of every other page's.
export const metricsTour: TourDefinition = {
	id: 'metrics',
	path: '/metrics',
	name: m.tourMetrics_name,
	version: 1,
	steps: [
		{ title: m.tourMetrics_welcomeTitle, body: m.tourMetrics_welcomeBody },
		{ target: 'metrics-mode', title: m.tourMetrics_modeTitle, body: m.tourMetrics_modeBody },
		{ target: 'metrics-time', title: m.tourMetrics_timeTitle, body: m.tourMetrics_timeBody },
		{ target: 'metrics-services', title: m.tourMetrics_servicesTitle, body: m.tourMetrics_servicesBody },
		{ target: 'metrics-group-by', title: m.tourMetrics_groupByTitle, body: m.tourMetrics_groupByBody },
		{ target: 'metrics-having', title: m.tourMetrics_havingTitle, body: m.tourMetrics_havingBody },
		{ target: 'metrics-functions', title: m.tourMetrics_functionsTitle, body: m.tourMetrics_functionsBody },
		{ target: 'metrics-compare', title: m.tourMetrics_compareTitle, body: m.tourMetrics_compareBody },
		{ target: 'metrics-time-shift', title: m.tourMetrics_timeShiftTitle, body: m.tourMetrics_timeShiftBody },
		{ target: 'metrics-auto-refresh', title: m.tourMetrics_autoRefreshTitle, body: m.tourMetrics_autoRefreshBody },
		{ target: 'metrics-catalog', title: m.tourMetrics_catalogTitle, body: m.tourMetrics_catalogBody },
		{ target: 'metrics-saved', title: m.tourMetrics_savedTitle, body: m.tourMetrics_savedBody },
		{ target: 'metrics-alert', title: m.tourMetrics_alertTitle, body: m.tourMetrics_alertBody },
		{ target: 'metrics-pin', title: m.tourMetrics_pinTitle, body: m.tourMetrics_pinBody },
		{ target: 'metrics-picker', title: m.tourMetrics_pickerTitle, body: m.tourMetrics_pickerBody, side: 'right' },
		{ target: 'metrics-formula-builder', title: m.tourMetrics_formulaBuilderTitle, body: m.tourMetrics_formulaBuilderBody, side: 'right' },
		{ target: 'metrics-chart', title: m.tourMetrics_chartTitle, body: m.tourMetrics_chartBody, side: 'top' },
		{ title: m.tourMetrics_doneTitle, body: m.tourMetrics_doneBody }
	]
};
