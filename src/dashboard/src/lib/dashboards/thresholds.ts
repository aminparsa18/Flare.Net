// Per-panel visual thresholds (roadmap's "Per-panel visual thresholds / conditional
// formatting" item) - purely *visual* rules ("color this red above X"), deliberately
// distinct from alert rules, which notify rather than style. Frontend-only: persisted as
// part of the panel inside the dashboard's opaque `layoutJson` blob, same as
// `yAxisMin`/`yAxisMax`, so there's no backend/schema change.
//
// Precedence: rules are *ordered*, and the first rule (in `DashboardPanel.thresholds`
// order) whose condition matches a value wins - see `matchThreshold`. That's the one place
// precedence matters (coloring a single value, e.g. a hovered tooltip reading); every
// rule's own line/region is drawn on the chart regardless of order.

export type ThresholdOperator = '>' | '>=' | '<' | '<=';

export const THRESHOLD_OPERATORS: readonly ThresholdOperator[] = ['>', '>=', '<', '<='];

/**
 * A fixed named palette, not free-form hex - every color here reads acceptably against
 * both the light and dark themes' chart backgrounds, which an arbitrary user-picked hex
 * wouldn't be guaranteed to.
 */
export type ThresholdColor = 'red' | 'orange' | 'yellow' | 'green' | 'blue' | 'purple';

export const THRESHOLD_COLORS: Record<ThresholdColor, string> = {
	red: '#ef4444',
	orange: '#f97316',
	yellow: '#eab308',
	green: '#22c55e',
	blue: '#3b82f6',
	purple: '#a855f7'
};

export interface PanelThreshold {
	id: string;
	operator: ThresholdOperator;
	/** In the metric's own raw (unscaled) unit - the same unit `yAxisMin`/`yAxisMax` use. */
	value: number;
	color: ThresholdColor;
}

export function thresholdMatches(threshold: PanelThreshold, value: number): boolean {
	switch (threshold.operator) {
		case '>':
			return value > threshold.value;
		case '>=':
			return value >= threshold.value;
		case '<':
			return value < threshold.value;
		case '<=':
			return value <= threshold.value;
	}
}

/** The first rule (list order) matching `value`, or `undefined` if none do - see this file's header for why first-match-wins. */
export function matchThreshold(thresholds: readonly PanelThreshold[] | undefined, value: number): PanelThreshold | undefined {
	return thresholds?.find((t) => thresholdMatches(t, value));
}

export function thresholdColorValue(color: ThresholdColor): string {
	return THRESHOLD_COLORS[color] ?? THRESHOLD_COLORS.red;
}

/** Whether a rule shades the region *above* its line (`>`/`>=`) or below it (`<`/`<=`). */
export function thresholdShadesAbove(threshold: PanelThreshold): boolean {
	return threshold.operator === '>' || threshold.operator === '>=';
}
