// Dashboard panel -> its explorer ("Open in Logs/Traces/Metrics" on DashboardPanelCard, and a
// click on a panel's chart). Frontend-only: each explorer already restores a whole saved-view
// state from `?state=` ($lib/deep-links.ts), so a link here is just that state, computed as
// the panel *currently shows it* - the saved query plus the dashboard's time-range override
// and variable selections, applied by the same rules DashboardLogsPanelBody/
// DashboardTracesPanelBody/DashboardMetricsPanelBody apply to their own private explorer
// (saved baseline, then the range preset, then the variables' services, then the variables'
// attributes appended to the panel's own). Computed from the panel definition rather than
// read back out of the panel body's explorer, so it works for a panel that hasn't scrolled
// into view (and lazily mounted its body) yet.

import { encodeStateDeepLinkParam } from '$lib/deep-links';
import { attributesForLogsPanel, attributesForTracesPanel, type ResolvedVariableOverrides } from './variables';
import { effectivePanelYAxisScale } from './visualization';
import type { AttributeFilter } from '$lib/api';
import { withBase } from '$lib/paths';
import type { DashboardPanel } from '$lib/dashboards-api';
import type { TimeRangePreset } from '$lib/logs/time-range';

type SavedState = Record<string, unknown>;

/** A clicked metric point opens this many buckets either side of it - a single bucket would be one lone point, not a chart. */
export const METRIC_POINT_WINDOW_BUCKETS = 5;

const EXPLORER_PATH: Record<DashboardPanel['panelType'], string> = {
	Logs: '/',
	Traces: '/traces',
	Metrics: '/metrics'
};

/** The saved-view state `panel`'s explorer should restore to show what the panel shows. */
export function panelExplorerState(
	panel: Pick<DashboardPanel, 'panelType' | 'query' | 'yAxisScale'>,
	timeRangeOverride: TimeRangePreset | null,
	overrides: ResolvedVariableOverrides
): SavedState {
	const state: SavedState = { ...((panel.query ?? {}) as SavedState) };
	if (timeRangeOverride) {
		state.timeRangePreset = timeRangeOverride;
		// Same rule every explorer's setTimeRangePreset applies - a custom range never lingers behind a preset.
		if (timeRangeOverride !== 'custom') state.customRange = null;
	}
	if (overrides.services.length) state.services = [...overrides.services];
	if (panel.panelType === 'Metrics') {
		// A panel's own scale setting outranks its query's - the explorer opens looking the same.
		state.yAxisScale = effectivePanelYAxisScale(panel);
		return state;
	}
	const attributes = panel.panelType === 'Logs' ? attributesForLogsPanel(overrides) : attributesForTracesPanel(overrides);
	if (attributes.length) {
		const saved = Array.isArray(state.attributeFilters) ? (state.attributeFilters as unknown[]) : [];
		state.attributeFilters = [...saved, ...attributes];
	}
	return state;
}

/** `state` narrowed to an explicit window. */
export function withCustomRange(state: SavedState, from: Date, to: Date): SavedState {
	return { ...state, timeRangePreset: 'custom', customRange: { from: from.toISOString(), to: to.toISOString() } };
}

/**
 * `state` (a Logs one) narrowed to one series of a grouped volume chart - `groupKey` as
 * VolumeChart's stacked segments carry it: `''` is events missing the attribute (an `Absent`
 * filter), `null` the server's rolled-up "other" series, which no single filter can express,
 * so it narrows nothing.
 */
export function withLogsGroup(state: SavedState, groupKey: string | null): SavedState {
	const groupBy = state.volumeGroupBy as { bag?: AttributeFilter['bag'] | 'BodyJson'; key?: string } | null | undefined;
	if (groupKey === null || !groupBy?.key) return state;
	if (groupBy.bag === 'BodyJson') {
		const saved = Array.isArray(state.bodyJsonFilters) ? (state.bodyJsonFilters as unknown[]) : [];
		const jsonFilter =
			groupKey === '' ? { path: groupBy.key, operator: 'Absent', value: '' } : { path: groupBy.key, operator: 'Equals', value: groupKey };
		return { ...state, bodyJsonFilters: [...saved, jsonFilter] };
	}
	const bag = groupBy.bag ?? 'Log';
	const filter: AttributeFilter =
		groupKey === '' ? { bag, key: groupBy.key, value: '', operator: 'Absent' } : { bag, key: groupBy.key, value: groupKey };
	const saved = Array.isArray(state.attributeFilters) ? (state.attributeFilters as unknown[]) : [];
	return { ...state, attributeFilters: [...saved, filter] };
}

/** The window a click on a metric point at `time` (a bucket start, epoch ms) opens - clamped to the range the panel queried. */
export function metricPointWindow(
	time: number,
	bucketSeconds: number,
	queried: { from: number; to: number } | null
): { from: Date; to: Date } {
	const widthMs = Math.max(1, bucketSeconds) * 1000;
	let from = time - METRIC_POINT_WINDOW_BUCKETS * widthMs;
	let to = time + (METRIC_POINT_WINDOW_BUCKETS + 1) * widthMs;
	if (queried) {
		from = Math.max(from, queried.from);
		to = Math.min(to, queried.to);
	}
	return { from: new Date(from), to: new Date(Math.max(to, from + widthMs)) };
}

export function panelExplorerHref(panelType: DashboardPanel['panelType'], state: SavedState): string {
	return withBase(`${EXPLORER_PATH[panelType]}?state=${encodeStateDeepLinkParam(state)}`);
}
