// Reactive state for one dashboard's viewer/editor page (routes/dashboards/[id]) -
// narrower than DashboardsState (the list page): loads exactly one dashboard by id.
// Phase 1 (see docs-internal/adr/0023-custom-dashboards.md) only ever mutated a panel
// list by removing one; Phase 2 (docs-internal/adr/0024-custom-dashboards-phase2-editor.md)
// adds `editing` (drives DashboardGrid.svelte's drag/resize), a dashboard-wide time-range
// override, and add/reposition/rename alongside remove. Phase 3 (roadmap's "Custom,
// user-built dashboards" item) adds an auto-refresh interval and the "set as home" toggle.
// Phase 4 added a dashboard-wide "Service" override - a fixed, always-on built-in variable,
// not persisted anywhere. Phase 5 (docs-internal/adr/0025-dashboard-variables.md) replaces
// that fixed override with real, user-defined `DashboardVariable`s: any number of them, each
// either backing `services` (like the old override) or an arbitrary attribute equality
// filter, with either a fixed custom value list or one resolved live from a query. A
// variable's *definition* is part of `dashboard.layout.variables` (persisted, like a panel);
// which value is currently *selected* stays session-only in `variableValues` below, same
// "never written back to the dashboard row" rule `timeRangeOverride` already follows.
//
// duplicatePanel()/exportPanel() (roadmap follow-up to Phase 3, which only covered a whole
// dashboard) are this class's own counterparts to DashboardsState's duplicate()/
// exportDashboard() - same "just another mutation against what's already loaded, no new
// API endpoint" shape, just scoped to one panel within `dashboard` instead of a whole
// dashboard in the list.

import { getDashboard, updateDashboard, type DashboardSummary, type DashboardPanel, type DashboardLayout, type DashboardRow, type DashboardVariable } from '$lib/dashboards-api';
import type { TimeRangePreset } from '$lib/logs/time-range';
import { type RefreshInterval, refreshIntervalMs } from './refresh-intervals';
import { getHomeDashboardId, setHomeDashboardId, clearHomeDashboardIdIfMatching } from './home-preference';
import { nextPanelPosition, panelsInRow } from './layout';
import { slugify } from './state.svelte';
import { downloadBlob } from '$lib/logs/export';
import { defaultSelection, resolveQueryVariableOptions, type VariableDependency } from './variables';
import type { PanelThreshold } from './thresholds';
import * as m from '$lib/paraglide/messages';

export class DashboardViewerState {
	dashboard = $state<DashboardSummary | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);
	removingPanelId = $state<string | null>(null);

	/** Drives DashboardGrid's drag/resize + the per-panel drag handle/rename/remove affordances - off by default so opening a dashboard never risks an accidental drag. */
	editing = $state(false);

	/**
	 * Fixed-duration preset overriding every panel's own saved time range, or `null` to
	 * leave each panel showing whatever range it was pinned/added with. Deliberately
	 * session-only - never written to `LayoutJson`/the dashboard row (see the ADR's
	 * "override is session-only" decision) - so it resets on reload rather than silently
	 * changing what a saved panel shows for everyone who opens this dashboard next.
	 */
	timeRangeOverride = $state<TimeRangePreset | null>(null);

	/**
	 * Mirrors `dashboard.layout.variables`, but is only ever reassigned by the methods
	 * that actually mean to change a variable (`load`/`saveVariable`/`removeVariable`) -
	 * never by `updateLayout`/`addPanel`/`renamePanel`/`removePanel`, even though those all
	 * reassign `dashboard` wholesale from a fresh server response (a new `layout.variables`
	 * array reference every time, JSON-round-tripped through `updateDashboard`, even when
	 * its *content* didn't change). `resolvedVariableOverrides` below derives from this
	 * field rather than `dashboard.layout.variables` directly for exactly that reason: a
	 * derived reading the latter would recompute to a new object on every unrelated panel
	 * drag/rename/add/remove, and every panel body's combined override effect (see
	 * `Dashboard*PanelBody.svelte`) would spuriously re-run - fully resetting every other
	 * panel's own live state (live-tail connection, selected event, scroll position, ...) -
	 * on an edit that never touched any variable. Passed straight down (with `variableValues`
	 * below) to `DashboardPanelCard.svelte`, which resolves each panel's own
	 * `ResolvedVariableOverrides` itself (factoring in that panel's own
	 * `excludedVariableIds`) rather than this class resolving one shared object for every
	 * panel - see `DashboardPanel.excludedVariableIds`'s own remarks.
	 */
	variables = $state<DashboardVariable[]>([]);

	/**
	 * Session-only selection for each of `variables` - keyed by `DashboardVariable.id`,
	 * `[]`/absent meaning "All" (that variable isn't currently narrowing anything). At most
	 * one value unless the variable is `multi`. Never persisted - see this file's header
	 * comment. Seeded from each variable's own default (see `defaultSelection`) in `load()`/
	 * whenever a variable is added.
	 */
	variableValues = $state<Record<string, string[]>>({});

	/** Resolved selectable values for each of `variables`, keyed by id - `Custom` variables'
	 *  own `customValues` verbatim, `Query` variables resolved live via
	 *  `resolveQueryVariableOptions` (see `./variables.ts`). Loaded once in `load()` and
	 *  again whenever the variable list itself changes (add/edit/remove below). */
	variableOptions = $state<Record<string, string[]>>({});

	/** Drives ManageVariablesDialog.svelte - `null` closed, `'new'` the blank-create form, else the variable being edited. */
	variableFormTarget = $state<DashboardVariable | 'new' | null>(null);

	/** Off by default - opening a dashboard never starts silently polling. Session-only,
	 *  same reasoning as timeRangeOverride above. */
	refreshInterval = $state<RefreshInterval>('off');

	/** Bumped once per refreshInterval tick. Panel bodies (DashboardLogsPanelBody etc.)
	 *  watch this prop and re-run their own query when it changes - the panels never call
	 *  back into this class, same one-way-down shape timeRangeOverride already uses. */
	refreshToken = $state(0);

	#refreshHandle: ReturnType<typeof setInterval> | null = null;

	/** Whether this dashboard is the one `/` loads instead of the Logs Explorer - mirrors
	 *  localStorage via home-preference.ts, re-read fresh in load() rather than kept in
	 *  sync reactively (there's exactly one place per page load that can change it: this
	 *  page's own toggleHome()). */
	isHome = $state(false);

	/**
	 * Ids of the rows currently collapsed on screen - seeded from each row's saved
	 * `collapsed` flag on load, then toggled by `toggleRowCollapsed`. Outside edit mode a
	 * toggle only changes this set (session-only, same rule as `timeRangeOverride`), so
	 * anyone viewing a dashboard can fold rows away without changing it for everyone else.
	 */
	collapsedRowIds = $state<Set<string>>(new Set());

	/** `dashboard.layout.rows`, or `[]` for a dashboard with none. */
	get rows(): DashboardRow[] {
		return this.dashboard?.layout.rows ?? [];
	}

	async load(id: string): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.dashboard = await getDashboard(id);
			this.isHome = getHomeDashboardId() === id;
			this.variables = this.dashboard.layout.variables;
			this.collapsedRowIds = new Set(this.rows.filter((r) => r.collapsed).map((r) => r.id));
			this.#reseedVariableValues();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
			clearHomeDashboardIdIfMatching(id);
		} finally {
			this.loading = false;
		}
		// Fire-and-forget, same as AlertRuleFormDialog's own onMount - each variable's
		// dropdown just shows fewer/no options until this resolves, not worth blocking the
		// dashboard itself (`loading` above) on.
		void this.#loadVariableOptions();
	}

	/** Seeds `variableValues` for every variable that doesn't have a selection yet - called
	 *  on load and after adding a variable, so a freshly-added variable starts at its own
	 *  default (or "All") instead of `undefined`. Never overwrites an existing selection
	 *  (editing/removing other variables shouldn't reset ones the user already picked a value
	 *  for) - except to trim it to one value when a variable was just switched from `multi`
	 *  back to single-value, which can't hold more. */
	#reseedVariableValues(): void {
		const next = { ...this.variableValues };
		for (const variable of this.variables) {
			const current = next[variable.id];
			if (!current) next[variable.id] = defaultSelection(variable);
			else if (!variable.multi && current.length > 1) next[variable.id] = current.slice(0, 1);
		}
		this.variableValues = next;
	}

	/**
	 * Resolves every variable's options, parents before their (possibly chained) dependents -
	 * see `DashboardVariable.dependsOnVariableId`. `#resolveOne` memoizes into `resolved` so a
	 * variable with more than one dependent isn't re-resolved once per dependent, and
	 * `#resolvingIds` breaks a cycle (a dependency loop a malformed/manually-edited layout
	 * could otherwise recurse on forever) by treating an in-progress parent as if it had none.
	 */
	async #loadVariableOptions(): Promise<void> {
		const byId = new Map(this.variables.map((v) => [v.id, v]));
		const resolved = new Map<string, string[]>();
		const resolvingIds = new Set<string>();

		const resolveOne = async (variable: DashboardVariable): Promise<string[]> => {
			const cached = resolved.get(variable.id);
			if (cached) return cached;
			if (variable.sourceKind === 'Custom') {
				const options = variable.customValues ?? [];
				resolved.set(variable.id, options);
				return options;
			}
			const options = await resolveQueryVariableOptions(variable, await this.#resolveDependency(variable, byId, resolveOne, resolvingIds));
			resolved.set(variable.id, options);
			return options;
		};

		const entries = await Promise.all(this.variables.map(async (v): Promise<[string, string[]]> => [v.id, await resolveOne(v)]));
		this.variableOptions = Object.fromEntries(entries);
	}

	/** Resolves `variable`'s own `dependsOnVariableId` (if any) into a `VariableDependency` -
	 *  ensuring the parent's options are themselves resolved first (so a chain more than one
	 *  level deep still resolves parent-before-child), and `undefined` for an unset/unknown/
	 *  currently-resolving (cyclic) parent or one with no value currently selected, matching
	 *  `resolveQueryVariableOptions`' own "no dependency means no narrowing" fallback. */
	async #resolveDependency(
		variable: DashboardVariable,
		byId: Map<string, DashboardVariable>,
		resolveOne: (v: DashboardVariable) => Promise<string[]>,
		resolvingIds: Set<string>
	): Promise<VariableDependency | undefined> {
		const parentId = variable.dependsOnVariableId;
		if (!parentId) return undefined;
		const parent = byId.get(parentId);
		if (!parent || resolvingIds.has(variable.id)) return undefined;
		resolvingIds.add(variable.id);
		try {
			await resolveOne(parent);
		} finally {
			resolvingIds.delete(variable.id);
		}
		const values = this.variableValues[parentId];
		return values?.length ? { variable: parent, values } : undefined;
	}

	setEditing(v: boolean): void {
		this.editing = v;
	}

	setTimeRangeOverride(preset: TimeRangePreset | null): void {
		this.timeRangeOverride = preset;
	}

	/** `values` is `[]` for "All"; a single-value variable only ever gets 0 or 1. */
	setVariableValues(variableId: string, values: string[]): void {
		this.variableValues = { ...this.variableValues, [variableId]: values };
		void this.#refreshDependentsOf(variableId);
	}

	/** Re-resolves the option list of every variable that directly `dependsOnVariableId`
	 *  `changedId` (and, recursively, theirs) after `changedId`'s own selected value changes -
	 *  chaining's whole point (see `DashboardVariable.dependsOnVariableId`). Any value of a
	 *  dependent's current selection that's no longer among its freshly-resolved options is
	 *  dropped (a single-value dependent thus resets to "All"), rather than left silently
	 *  narrowing a panel by a value that's no longer actually offered. */
	async #refreshDependentsOf(changedId: string): Promise<void> {
		const parent = this.variables.find((v) => v.id === changedId);
		const parentValues = this.variableValues[changedId];
		const dependency: VariableDependency | undefined = parent && parentValues?.length ? { variable: parent, values: parentValues } : undefined;
		const dependents = this.variables.filter((v) => v.dependsOnVariableId === changedId && v.sourceKind === 'Query');
		for (const dependent of dependents) {
			const options = await resolveQueryVariableOptions(dependent, dependency);
			this.variableOptions = { ...this.variableOptions, [dependent.id]: options };
			const current = this.variableValues[dependent.id] ?? [];
			const kept = current.filter((v) => options.includes(v));
			if (kept.length !== current.length) {
				this.variableValues = { ...this.variableValues, [dependent.id]: kept };
			}
			await this.#refreshDependentsOf(dependent.id);
		}
	}

	setRefreshInterval(value: RefreshInterval): void {
		this.refreshInterval = value;
		this.#stopAutoRefresh();
		const ms = refreshIntervalMs(value);
		if (ms != null) {
			this.#refreshHandle = setInterval(() => {
				this.refreshToken++;
			}, ms);
		}
	}

	#stopAutoRefresh(): void {
		if (this.#refreshHandle !== null) {
			clearInterval(this.#refreshHandle);
			this.#refreshHandle = null;
		}
	}

	toggleHome(): void {
		if (!this.dashboard) return;
		const next = this.isHome ? null : this.dashboard.id;
		setHomeDashboardId(next);
		this.isHome = next != null;
	}

	/** Clears the auto-refresh interval - call from the route's onDestroy so leaving the
	 *  page (or navigating to a different dashboard) doesn't leave a timer ticking against
	 *  a torn-down panel tree. */
	dispose(): void {
		this.#stopAutoRefresh();
	}

	/** Persists `layout`, carrying `dashboard.layout.variables`/`rows` forward untouched
	 *  unless the caller's `layout` already specifies its own - most layout-mutating methods
	 *  below (updateLayout/addPanel/renamePanel/removePanel) only ever mean to touch `panels`,
	 *  so without this a drag/resize would silently wipe out every variable definition and
	 *  row the next time it fired. */
	async #saveLayout(layout: Pick<DashboardLayout, 'panels'> & Partial<Pick<DashboardLayout, 'variables' | 'rows'>>): Promise<DashboardSummary | null> {
		const dashboard = this.dashboard;
		if (!dashboard) return null;
		const full: DashboardLayout = {
			panels: layout.panels,
			variables: layout.variables ?? dashboard.layout.variables,
			rows: layout.rows ?? dashboard.layout.rows ?? []
		};
		return updateDashboard(dashboard.id, { name: dashboard.name, description: dashboard.description, layout: full });
	}

	/**
	 * Applies DashboardGrid's `change` event (fired once per completed drag/resize).
	 * Updates `dashboard.layout` optimistically *before* awaiting the PUT, unlike
	 * addPanel/renamePanel/removePanel below - a drag/resize is high-frequency enough
	 * (one call per interaction, but interactions can come in quick succession while
	 * someone tidies a layout) that waiting for each round-trip before reflecting it
	 * locally would make the grid visibly lag behind the user's own last drop, and a
	 * second edit landing before the first PUT resolves would otherwise get clobbered by
	 * that first response overwriting `dashboard` out from under it.
	 */
	async updateLayout(changes: { id: string; layout: DashboardPanel['layout'] }[]): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		const byId = new Map(changes.map((c) => [c.id, c.layout]));
		const panels = dashboard.layout.panels.map((p) => (byId.has(p.id) ? { ...p, layout: byId.get(p.id)! } : p));
		this.dashboard = { ...dashboard, layout: { ...dashboard.layout, panels } };
		try {
			await this.#saveLayout({ panels });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async addPanel(panel: DashboardPanel): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		try {
			this.dashboard = await this.#saveLayout({ panels: [...dashboard.layout.panels, panel] });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async renamePanel(panelId: string, title: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		try {
			this.dashboard = await this.#saveLayout({ panels: dashboard.layout.panels.map((p) => (p.id === panelId ? { ...p, title } : p)) });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/** Sets/clears `panelId`'s `DashboardPanel.description` - persisted through `#saveLayout`
	 *  the same way renamePanel does. Blank (after trimming) clears the field rather than
	 *  saving `''`. */
	async setPanelDescription(panelId: string, description: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		const next = description.trim() || undefined;
		try {
			this.dashboard = await this.#saveLayout({ panels: dashboard.layout.panels.map((p) => (p.id === panelId ? { ...p, description: next } : p)) });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/** Toggles whether `panelId` opts out of `variableId`'s narrowing (see
	 *  `DashboardPanel.excludedVariableIds`) - the per-panel counterpart to `setVariableValue`
	 *  below, but a layout-level field (persisted per panel, like `title`) rather than a
	 *  session-only selection, so it goes through `#saveLayout` the same way renamePanel does
	 *  rather than just reassigning local state. */
	async setPanelVariableExcluded(panelId: string, variableId: string, excluded: boolean): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		try {
			this.dashboard = await this.#saveLayout({
				panels: dashboard.layout.panels.map((p) => {
					if (p.id !== panelId) return p;
					const current = p.excludedVariableIds ?? [];
					const next = excluded ? (current.includes(variableId) ? current : [...current, variableId]) : current.filter((id) => id !== variableId);
					return { ...p, excludedVariableIds: next.length ? next : undefined };
				})
			});
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/** Sets/clears `panelId`'s soft Y-axis min/max override (`DashboardPanel.yAxisMin`/
	 *  `yAxisMax` - roadmap's "Soft Y-axis min/max on metric charts" item) - a layout-level
	 *  field (persisted per panel, like `title`/`excludedVariableIds`), so it goes through
	 *  `#saveLayout` the same way `setPanelVariableExcluded` above does rather than
	 *  local-only state. `null` for either bound means "auto". */
	async setPanelYAxisBounds(panelId: string, yAxisMin: number | null, yAxisMax: number | null): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		try {
			this.dashboard = await this.#saveLayout({
				panels: dashboard.layout.panels.map((p) => (p.id === panelId ? { ...p, yAxisMin: yAxisMin ?? undefined, yAxisMax: yAxisMax ?? undefined } : p))
			});
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/** Replaces `panelId`'s ordered visual threshold rules (`DashboardPanel.thresholds`) -
	 *  persisted through `#saveLayout` exactly like `setPanelYAxisBounds` above. An empty
	 *  list clears the field rather than saving `[]`. */
	async setPanelThresholds(panelId: string, thresholds: PanelThreshold[]): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		try {
			this.dashboard = await this.#saveLayout({
				panels: dashboard.layout.panels.map((p) => (p.id === panelId ? { ...p, thresholds: thresholds.length ? thresholds : undefined } : p))
			});
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async removePanel(panelId: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		this.removingPanelId = panelId;
		try {
			this.dashboard = await this.#saveLayout({ panels: dashboard.layout.panels.filter((p) => p.id !== panelId) });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.removingPanelId = null;
		}
	}

	/** Appends an independent copy of `panelId` (new id, "(copy)" title, placed below every
	 *  existing panel in the original's own row per nextPanelPosition - same placement
	 *  AddPanelDialog gives a brand-new panel) right after it in the same dashboard. Mirrors DashboardsState.duplicate(), just
	 *  one panel instead of a whole dashboard, and going through addPanel's own PUT rather
	 *  than a fresh createDashboard() call since there's no new dashboard here. */
	async duplicatePanel(panelId: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		const original = dashboard.layout.panels.find((p) => p.id === panelId);
		if (!original) return;
		await this.addPanel({
			...original,
			id: crypto.randomUUID(),
			title: m.dashboardTable_duplicateName({ name: original.title }),
			layout: nextPanelPosition(panelsInRow(dashboard.layout.panels, this.rows, this.#effectiveRowId(original)))
		});
	}

	/** Downloads `panelId`'s definition (type/title/description/size/query - never any cached query
	 *  result, same "definitions only" rule exportDashboard() follows) as a JSON file.
	 *  Position (x/y) is deliberately omitted - it's only meaningful within this dashboard's
	 *  own grid, not something a copy elsewhere could reuse. No import path for this file
	 *  exists yet, same as exportDashboard() before Phase 3's import follow-up landed. */
	exportPanel(panelId: string): void {
		const panel = this.dashboard?.layout.panels.find((p) => p.id === panelId);
		if (!panel) return;
		const body = { panelType: panel.panelType, title: panel.title, description: panel.description, layout: { w: panel.layout.w, h: panel.layout.h }, query: panel.query };
		const blob = new Blob([JSON.stringify(body, null, 2)], { type: 'application/json;charset=utf-8' });
		downloadBlob(blob, `flare-dashboard-panel_${slugify(panel.title, 'panel')}.json`);
	}

	// ---- Rows (DashboardRowSection.svelte) ---------------------------------------------

	/** `panel.rowId` if it names a row that still exists, else `null` (ungrouped) - see `panelsInRow`. */
	#effectiveRowId(panel: DashboardPanel): string | null {
		return panel.rowId && this.rows.some((r) => r.id === panel.rowId) ? panel.rowId : null;
	}

	/** Appends a new, empty, expanded row below every existing one. */
	async addRow(title: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		try {
			this.dashboard = await this.#saveLayout({ panels: dashboard.layout.panels, rows: [...this.rows, { id: crypto.randomUUID(), title }] });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async renameRow(rowId: string, title: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		try {
			this.dashboard = await this.#saveLayout({ panels: dashboard.layout.panels, rows: this.rows.map((r) => (r.id === rowId ? { ...r, title } : r)) });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/** Swaps `rowId` with its neighbor above (`-1`) or below (`1`); a no-op at either end. */
	async moveRow(rowId: string, direction: -1 | 1): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		const rows = [...this.rows];
		const index = rows.findIndex((r) => r.id === rowId);
		const target = index + direction;
		if (index < 0 || target < 0 || target >= rows.length) return;
		[rows[index], rows[target]] = [rows[target], rows[index]];
		try {
			this.dashboard = await this.#saveLayout({ panels: dashboard.layout.panels, rows });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/**
	 * Removes a row but never its panels - they move to the ungrouped area, stacked below
	 * whatever is already there with their relative arrangement kept (every one shifted
	 * down by the same amount), so deleting a row can't silently delete panels with it.
	 */
	async removeRow(rowId: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		const ungrouped = panelsInRow(dashboard.layout.panels, this.rows, null);
		const offset = nextPanelPosition(ungrouped).y;
		const panels = dashboard.layout.panels.map((p) =>
			this.#effectiveRowId(p) === rowId ? { ...p, rowId: undefined, layout: { ...p.layout, y: p.layout.y + offset } } : p
		);
		try {
			this.dashboard = await this.#saveLayout({ panels, rows: this.rows.filter((r) => r.id !== rowId) });
			const collapsed = new Set(this.collapsedRowIds);
			collapsed.delete(rowId);
			this.collapsedRowIds = collapsed;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/** Moves `panelId` into `rowId` (`null` = ungrouped), placed below that row's existing
	 *  panels at its current size. */
	async movePanelToRow(panelId: string, rowId: string | null): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		const panel = dashboard.layout.panels.find((p) => p.id === panelId);
		if (!panel || this.#effectiveRowId(panel) === rowId) return;
		const { y } = nextPanelPosition(panelsInRow(dashboard.layout.panels, this.rows, rowId));
		const moved: DashboardPanel = { ...panel, rowId: rowId ?? undefined, layout: { ...panel.layout, x: 0, y } };
		try {
			this.dashboard = await this.#saveLayout({ panels: dashboard.layout.panels.map((p) => (p.id === panelId ? moved : p)) });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/**
	 * Collapses/expands `rowId` on screen. In edit mode the new state is also saved as the
	 * row's `collapsed` default (what everyone sees on open); outside edit mode it's
	 * session-only - viewing a dashboard never changes it, same rule drag/resize follows.
	 */
	async toggleRowCollapsed(rowId: string): Promise<void> {
		const collapsed = new Set(this.collapsedRowIds);
		const next = !collapsed.has(rowId);
		if (next) collapsed.add(rowId);
		else collapsed.delete(rowId);
		this.collapsedRowIds = collapsed;

		const dashboard = this.dashboard;
		if (!dashboard || !this.editing) return;
		try {
			this.dashboard = await this.#saveLayout({
				panels: dashboard.layout.panels,
				rows: this.rows.map((r) => (r.id === rowId ? { ...r, collapsed: next || undefined } : r))
			});
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	// ---- Variables (ManageVariablesDialog.svelte) -------------------------------------

	openCreateVariable(): void {
		this.variableFormTarget = 'new';
	}

	openEditVariable(variable: DashboardVariable): void {
		this.variableFormTarget = variable;
	}

	closeVariableForm(): void {
		this.variableFormTarget = null;
	}

	/** Adds or updates one variable definition, persists it, then re-resolves every
	 *  variable's options (a rename/target/query change can change what this one variable's
	 *  own dropdown should offer) and re-seeds `variableValues` for a brand-new variable. */
	async saveVariable(variable: DashboardVariable): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		const exists = this.variables.some((v) => v.id === variable.id);
		const variables = exists ? this.variables.map((v) => (v.id === variable.id ? variable : v)) : [...this.variables, variable];
		try {
			this.dashboard = await this.#saveLayout({ panels: dashboard.layout.panels, variables });
			this.variables = variables;
			this.variableFormTarget = null;
			this.#reseedVariableValues();
			await this.#loadVariableOptions();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	/** Removes one variable definition and its session-only selection/options - any panel
	 *  currently narrowed by it just stops being narrowed, same as turning Phase 4's fixed
	 *  Service override off. Any other variable that chained off this one (`dependsOnVariableId
	 *  === variableId`, see `DashboardVariable.dependsOnVariableId`) has that link cleared too,
	 *  same "off means don't touch this filter at all" fallback a missing/unselected parent
	 *  already gets in `resolveQueryVariableOptions` - left pointing at a deleted id, it would
	 *  otherwise silently resolve as unchained forever (harmless, since `#loadVariableOptions`
	 *  already treats an unknown parent id as "no dependency") but never say so in the form.
	 *  Any panel's own `excludedVariableIds` referencing this variable is cleaned up the same
	 *  way - a dangling opt-out is just as harmless as a dangling `dependsOnVariableId`, but
	 *  there's no reason to keep carrying it once the variable it names is gone.
	 */
	async removeVariable(variableId: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		const variables = this.variables.filter((v) => v.id !== variableId).map((v) => (v.dependsOnVariableId === variableId ? { ...v, dependsOnVariableId: null } : v));
		const panels = dashboard.layout.panels.map((p) =>
			p.excludedVariableIds?.includes(variableId) ? { ...p, excludedVariableIds: p.excludedVariableIds.filter((id) => id !== variableId) } : p
		);
		try {
			this.dashboard = await this.#saveLayout({ panels, variables });
			this.variables = variables;
			const { [variableId]: _removedValue, ...restValues } = this.variableValues;
			const { [variableId]: _removedOptions, ...restOptions } = this.variableOptions;
			this.variableValues = restValues;
			this.variableOptions = restOptions;
			await this.#loadVariableOptions();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
