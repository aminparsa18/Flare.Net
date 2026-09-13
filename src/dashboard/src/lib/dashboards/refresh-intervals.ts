// Auto-refresh interval options for a dashboard viewer (Phase 3, see
// docs-internal/planning/roadmap.md's "Custom, user-built dashboards" item and
// ../../../docs/how-to/build-custom-dashboards.md). Unlike Metrics'/Traces' own
// autoRefreshEnabled (a fixed-30s on/off checkbox - see MetricsExplorerState/
// TracesExplorerState), a dashboard offers a few different intervals since it's
// usually left open on a screen rather than actively worked in.
//
// Session-only, same as DashboardViewerState.timeRangeOverride - never written to
// LayoutJson/the dashboard row, so it resets on reload rather than silently changing
// what anyone else sees when they open this dashboard.
//
// `label` is deliberately NOT stored here, same reasoning TIME_RANGE_PRESETS's own
// header comment gives - call refreshIntervalLabel() fresh on every render instead of
// caching a `.label` string that would go stale on a later locale switch.
import * as m from '$lib/paraglide/messages';

export type RefreshInterval = 'off' | '15s' | '30s' | '1m' | '5m';

export const REFRESH_INTERVALS: { value: RefreshInterval; ms: number | null }[] = [
	{ value: 'off', ms: null },
	{ value: '15s', ms: 15_000 },
	{ value: '30s', ms: 30_000 },
	{ value: '1m', ms: 60_000 },
	{ value: '5m', ms: 300_000 }
];

const LABELS: Record<RefreshInterval, () => string> = {
	off: m.dashboardViewer_refreshOff,
	'15s': m.dashboardViewer_refresh15s,
	'30s': m.dashboardViewer_refresh30s,
	'1m': m.dashboardViewer_refresh1m,
	'5m': m.dashboardViewer_refresh5m
};

export function refreshIntervalLabel(value: RefreshInterval): string {
	return LABELS[value]();
}

export function refreshIntervalMs(value: RefreshInterval): number | null {
	return REFRESH_INTERVALS.find((r) => r.value === value)?.ms ?? null;
}
