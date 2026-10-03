// Per-panel legend placement and per-series color overrides (roadmap's "Panel legend
// placement + per-series colors" item). Frontend-only, persisted inside the dashboard's
// opaque `layoutJson` like `thresholds` - no backend/schema change.
//
// Overrides are keyed by `seriesLabel` from `$lib/dashboards/visualization` (service name +
// every attribute), never a compacted display label - that one depends on which other series
// happen to be in the result, so it isn't a stable identity. MetricChart formats its own
// labels differently, so it looks overrides up through `seriesLabel` too, not its own.
//
// Colors come from the thresholds' fixed named palette rather than free-form hex, for the same
// reason that palette exists: every entry reads on both the light and dark chart backgrounds.

import type { MetricSeries } from '$lib/metrics-api';
import { THRESHOLD_COLORS, thresholdColorValue, type ThresholdColor } from './thresholds';

export type LegendPosition = 'bottom' | 'right' | 'hidden';

export const LEGEND_POSITIONS: readonly LegendPosition[] = ['bottom', 'right', 'hidden'];

/** Lenient read of `DashboardPanel.legendPosition` - `undefined` for unset or unrecognised,
 *  so each visualization keeps its own default (below the chart; beside it for a pie). */
export function parseLegendPosition(raw: unknown): LegendPosition | undefined {
	return LEGEND_POSITIONS.includes(raw as LegendPosition) ? (raw as LegendPosition) : undefined;
}

/** Lenient read of `DashboardPanel.seriesColors` - entries whose value isn't a palette name are dropped. */
export function parseSeriesColors(raw: unknown): Record<string, ThresholdColor> {
	const out: Record<string, ThresholdColor> = {};
	if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) return out;
	for (const [key, value] of Object.entries(raw)) {
		if (typeof value === 'string' && value in THRESHOLD_COLORS) out[key] = value as ThresholdColor;
	}
	return out;
}

/**
 * Classes for a chart + legend pair, shared by every legend-drawing chart so "right" reads
 * the same on each: `wrapper` goes on the element holding both, `plot` on the chart's own box,
 * `legend` on the legend list. The right-hand legend is a fixed-width column capped at the
 * charts' 180px plot height, scrolling past that rather than stretching the panel.
 */
export function legendLayout(position: Exclude<LegendPosition, 'hidden'>): { wrapper: string; plot: string; legend: string } {
	return position === 'right'
		? { wrapper: 'flex-row items-start gap-3', plot: 'min-w-0 flex-1', legend: 'flex max-h-[180px] w-40 shrink-0 flex-col gap-1 overflow-y-auto' }
		: { wrapper: 'flex-col', plot: 'min-w-0', legend: 'mt-2 flex flex-wrap gap-x-4 gap-y-1' };
}

/** The overridden CSS color for the series `key`, or `undefined` to keep its default. */
export function seriesColorOverride(overrides: Readonly<Record<string, ThresholdColor>> | undefined, key: string): string | undefined {
	const color = overrides?.[key];
	return color ? thresholdColorValue(color) : undefined;
}

/** Example shown as the template input's placeholder - not a default, an unset template keeps the automatic labels. */
export const LEGEND_FORMAT_EXAMPLE = '{{service}} {{http.route}}';

/** Lenient read of `DashboardPanel.legendFormat` - `undefined` for unset, blank or non-string. */
export function parseLegendFormat(raw: unknown): string | undefined {
	return typeof raw === 'string' && raw.trim() !== '' ? raw : undefined;
}

/**
 * Renders a legend template for one series: `{{service}}` is the service name, any other
 * `{{key}}` is that attribute's value, and a key the series doesn't carry renders empty.
 * A template that renders to nothing but whitespace returns `null` so the caller falls back
 * to its automatic label rather than drawing a blank legend entry. Only the displayed text
 * is affected - color overrides stay keyed on `seriesLabel`.
 */
export function formatSeriesLabel(template: string | undefined, series: Pick<MetricSeries, 'serviceName' | 'attributes'>): string | null {
	if (!template) return null;
	const rendered = template
		.replace(/\{\{\s*([^{}]+?)\s*\}\}/g, (_, key: string) => (key === 'service' ? series.serviceName : (series.attributes[key] ?? '')))
		.replace(/\s+/g, ' ')
		.trim();
	return rendered === '' ? null : rendered;
}
