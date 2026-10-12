// "Report mode" is how Flare.AlertWorker renders a dashboard for a scheduled report (ADR-0142): its headless
// browser opens `/dashboards/<id>?report=1&...`. In that mode the page drops its toolbar and app chrome, expands
// every row, and loads every panel at once instead of waiting for it to scroll into view (a headless browser
// never scrolls), then marks <html data-report-ready> so the renderer knows when to start waiting for the charts.
// Pure and DOM-light so the flag can be read from any component without threading a prop through the grid.

export const REPORT_PARAM = 'report';
const READY_ATTRIBUTE = 'reportReady';

export function isReportSearch(search: URLSearchParams): boolean {
	return search.get(REPORT_PARAM) === '1';
}

/** True when the current page was opened in report mode. Always false during SSR. */
export function isReportMode(): boolean {
	return typeof window !== 'undefined' && isReportSearch(new URLSearchParams(window.location.search));
}

/** Tells the renderer the dashboard has loaded and every panel has been asked to render. */
export function markReportReady(): void {
	if (typeof document !== 'undefined') document.documentElement.dataset[READY_ATTRIBUTE] = '1';
}
