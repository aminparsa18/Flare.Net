// Shared panel-placement math for the two places a brand-new panel gets a `layout`
// (`{x,y,w,h}`) assigned: PinToDashboardDialog.svelte (pinning from an Explorer toolbar)
// and AddPanelDialog.svelte (adding in-place from the dashboard editor). Both want the
// same "don't overlap what's already there" behavior, so it lives here once rather than
// being duplicated inline in each dialog.

import type { DashboardPanel } from '$lib/dashboards-api';

/** Columns in the dashboard grid (DashboardGrid.svelte's `GridStack.init({ column })`). */
export const GRID_COLUMNS = 12;

/** Default new-panel size - half-width so panels tile two-up by default instead of every panel stacking full-width. */
const DEFAULT_WIDTH = 6;
const DEFAULT_HEIGHT = 4;

/**
 * Places a new panel below every existing one (`y = max(existing y+h)`), left-aligned -
 * simple and always non-overlapping, unlike gridstack's own `autoPosition` (which packs
 * into gaps and would require the grid instance to already exist). Gridstack's `float`
 * mode then lets the user immediately drag it wherever they actually want once it lands.
 */
export function nextPanelPosition(existing: readonly DashboardPanel[]): DashboardPanel['layout'] {
	const y = existing.reduce((max, p) => Math.max(max, p.layout.y + p.layout.h), 0);
	return { x: 0, y, w: DEFAULT_WIDTH, h: DEFAULT_HEIGHT };
}
