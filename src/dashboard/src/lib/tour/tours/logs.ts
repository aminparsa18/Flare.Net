import type { TourDefinition } from '#lib/tour/types.ts';
import * as m from '#lib/paraglide/messages.js';

// Order follows the page's reading order: toolbar row 1, row 2, the optional AI bar, then the
// sidebar, the stacked sections above the table, and the table itself. Each `target` is a
// `data-tour` wrapper in LogsToolbar.svelte / routes/+page.svelte.
export const logsTour: TourDefinition = {
	id: 'logs',
	path: '/',
	name: m.tourLogs_name,
	version: 1,
	steps: [
		{ title: m.tourLogs_welcomeTitle, body: m.tourLogs_welcomeBody },
		{ target: 'logs-time-range', title: m.tourLogs_timeRangeTitle, body: m.tourLogs_timeRangeBody },
		{ target: 'logs-service-level', title: m.tourLogs_serviceLevelTitle, body: m.tourLogs_serviceLevelBody },
		{ target: 'logs-scope-trace', title: m.tourLogs_scopeTraceTitle, body: m.tourLogs_scopeTraceBody },
		{ target: 'logs-functions', title: m.tourLogs_functionsTitle, body: m.tourLogs_functionsBody },
		{ target: 'logs-time-shift', title: m.tourLogs_timeShiftTitle, body: m.tourLogs_timeShiftBody },
		{ target: 'logs-display', title: m.tourLogs_displayTitle, body: m.tourLogs_displayBody },
		{ target: 'logs-clear', title: m.tourLogs_clearTitle, body: m.tourLogs_clearBody },
		{ target: 'logs-saved', title: m.tourLogs_savedTitle, body: m.tourLogs_savedBody },
		{ target: 'logs-pin-alert', title: m.tourLogs_pinAlertTitle, body: m.tourLogs_pinAlertBody },
		{ target: 'logs-metric', title: m.tourLogs_metricTitle, body: m.tourLogs_metricBody },
		{ target: 'logs-patterns', title: m.tourLogs_patternsTitle, body: m.tourLogs_patternsBody },
		{ target: 'logs-export-share', title: m.tourLogs_exportShareTitle, body: m.tourLogs_exportShareBody },
		{ target: 'logs-search', title: m.tourLogs_searchTitle, body: m.tourLogs_searchBody },
		{ target: 'logs-all-fields', title: m.tourLogs_allFieldsTitle, body: m.tourLogs_allFieldsBody },
		{ target: 'logs-live', title: m.tourLogs_liveTitle, body: m.tourLogs_liveBody, side: 'left' },
		{ target: 'logs-ai', title: m.tourLogs_aiTitle, body: m.tourLogs_aiBody },
		{ target: 'logs-facets', title: m.tourLogs_facetsTitle, body: m.tourLogs_facetsBody, side: 'right' },
		{ target: 'logs-volume', title: m.tourLogs_volumeTitle, body: m.tourLogs_volumeBody },
		{ target: 'logs-value-distribution', title: m.tourLogs_valueDistributionTitle, body: m.tourLogs_valueDistributionBody },
		{ target: 'logs-attribute-filters', title: m.tourLogs_attributeFiltersTitle, body: m.tourLogs_attributeFiltersBody },
		{ target: 'logs-json-filters', title: m.tourLogs_jsonFiltersTitle, body: m.tourLogs_jsonFiltersBody },
		{ target: 'logs-sql', title: m.tourLogs_sqlTitle, body: m.tourLogs_sqlBody },
		{ target: 'logs-table', title: m.tourLogs_tableTitle, body: m.tourLogs_tableBody, side: 'top' },
		{ title: m.tourLogs_doneTitle, body: m.tourLogs_doneBody }
	]
};
