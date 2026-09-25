// Shared panel-placement math for the two places a brand-new panel gets a `layout`
// (`{x,y,w,h}`) assigned: PinToDashboardDialog.svelte (pinning from an Explorer toolbar)
// and AddPanelDialog.svelte (adding in-place from the dashboard editor). Both want the
// same "don't overlap what's already there" behavior, so it lives here once rather than
// being duplicated inline in each dialog.

import type { DashboardPanel, DashboardRow } from '$lib/dashboards-api';

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

/**
 * The panels in one grid section: `rowId === null` is the ungrouped area above every row.
 * A panel whose `rowId` names no row in `rows` (the row was removed) counts as ungrouped,
 * so it can never become unreachable. Each section is its own gridstack grid, so
 * `nextPanelPosition` should only ever be given one section's panels.
 */
export function panelsInRow(panels: readonly DashboardPanel[], rows: readonly DashboardRow[], rowId: string | null): DashboardPanel[] {
	const rowIds = new Set(rows.map((r) => r.id));
	return panels.filter((p) => {
		const effective = p.rowId && rowIds.has(p.rowId) ? p.rowId : null;
		return effective === rowId;
	});
}
