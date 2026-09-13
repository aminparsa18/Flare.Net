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

import { getDashboard, updateDashboard, type DashboardSummary, type DashboardPanel, type DashboardLayout, type DashboardVariable } from '$lib/dashboards-api';
import type { TimeRangePreset } from '$lib/logs/time-range';
import { type RefreshInterval, refreshIntervalMs } from './refresh-intervals';
import { getHomeDashboardId, setHomeDashboardId, clearHomeDashboardIdIfMatching } from './home-preference';
import { nextPanelPosition } from './layout';
import { slugify } from './state.svelte';
import { downloadBlob } from '$lib/logs/export';
import { resolveQueryVariableOptions, resolveVariableOverrides, type ResolvedVariableOverrides } from './variables';
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
	 * on an edit that never touched any variable.
	 */
	variables = $state<DashboardVariable[]>([]);

	/**
	 * Session-only selection for each of `variables` - keyed by `DashboardVariable.id`,
	 * `null`/absent meaning "All" (that variable isn't currently narrowing anything). Never
	 * persisted - see this file's header comment. Seeded from each variable's own
	 * `defaultValue` in `load()`/whenever a variable is added.
	 */
	variableValues = $state<Record<string, string | null>>({});

	/** Resolved selectable values for each of `variables`, keyed by id - `Custom` variables'
	 *  own `customValues` verbatim, `Query` variables resolved live via
	 *  `resolveQueryVariableOptions` (see `./variables.ts`). Loaded once in `load()` and
	 *  again whenever the variable list itself changes (add/edit/remove below). */
	variableOptions = $state<Record<string, string[]>>({});

	/** Every currently-selected variable value, already resolved into the shape panel
	 *  bodies apply (see `./variables.ts`) - recomputed whenever `variables` or
	 *  `variableValues` changes, and *only* then (see `variables`' own remarks above).
	 *  Replaces Phase 4's single `serviceOverride` prop threaded through DashboardGrid ->
	 *  DashboardPanelCard -> each panel body. */
	resolvedVariableOverrides = $derived<ResolvedVariableOverrides>(resolveVariableOverrides(this.variables, this.variableValues));

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

	async load(id: string): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.dashboard = await getDashboard(id);
			this.isHome = getHomeDashboardId() === id;
			this.variables = this.dashboard.layout.variables;
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
	 *  `defaultValue` (or "All") instead of `undefined`. Never overwrites an existing
	 *  selection (editing/removing other variables shouldn't reset ones the user already
	 *  picked a value for). */
	#reseedVariableValues(): void {
		const next = { ...this.variableValues };
		for (const variable of this.variables) {
			if (!(variable.id in next)) next[variable.id] = variable.defaultValue ?? null;
		}
		this.variableValues = next;
	}

	async #loadVariableOptions(): Promise<void> {
		const entries = await Promise.all(
			this.variables.map(async (v): Promise<[string, string[]]> => [v.id, v.sourceKind === 'Custom' ? (v.customValues ?? []) : await resolveQueryVariableOptions(v)])
		);
		this.variableOptions = Object.fromEntries(entries);
	}

	setEditing(v: boolean): void {
		this.editing = v;
	}

	setTimeRangeOverride(preset: TimeRangePreset | null): void {
		this.timeRangeOverride = preset;
	}

	setVariableValue(variableId: string, value: string | null): void {
		this.variableValues = { ...this.variableValues, [variableId]: value };
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

	/** Persists `layout`, carrying `dashboard.layout.variables` forward untouched unless the
	 *  caller's `layout` already specifies its own - every layout-mutating method below
	 *  (updateLayout/addPanel/renamePanel/removePanel) only ever means to touch `panels`, so
	 *  without this a drag/resize would silently wipe out every variable definition the next
	 *  time it fired. */
	async #saveLayout(layout: Pick<DashboardLayout, 'panels'> & Partial<Pick<DashboardLayout, 'variables'>>): Promise<DashboardSummary | null> {
		const dashboard = this.dashboard;
		if (!dashboard) return null;
		const full: DashboardLayout = { panels: layout.panels, variables: layout.variables ?? dashboard.layout.variables };
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
		this.dashboard = { ...dashboard, layout: { panels, variables: dashboard.layout.variables } };
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
	 *  existing panel per nextPanelPosition - same placement AddPanelDialog gives a brand-new
	 *  panel) right after it in the same dashboard. Mirrors DashboardsState.duplicate(), just
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
			layout: nextPanelPosition(dashboard.layout.panels)
		});
	}

	/** Downloads `panelId`'s definition (type/title/size/query - never any cached query
	 *  result, same "definitions only" rule exportDashboard() follows) as a JSON file.
	 *  Position (x/y) is deliberately omitted - it's only meaningful within this dashboard's
	 *  own grid, not something a copy elsewhere could reuse. No import path for this file
	 *  exists yet, same as exportDashboard() before Phase 3's import follow-up landed. */
	exportPanel(panelId: string): void {
		const panel = this.dashboard?.layout.panels.find((p) => p.id === panelId);
		if (!panel) return;
		const body = { panelType: panel.panelType, title: panel.title, layout: { w: panel.layout.w, h: panel.layout.h }, query: panel.query };
		const blob = new Blob([JSON.stringify(body, null, 2)], { type: 'application/json;charset=utf-8' });
		downloadBlob(blob, `flare-dashboard-panel_${slugify(panel.title, 'panel')}.json`);
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
	 *  Service override off. */
	async removeVariable(variableId: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		const variables = this.variables.filter((v) => v.id !== variableId);
		try {
			this.dashboard = await this.#saveLayout({ panels: dashboard.layout.panels, variables });
			this.variables = variables;
			const { [variableId]: _removedValue, ...restValues } = this.variableValues;
			const { [variableId]: _removedOptions, ...restOptions } = this.variableOptions;
			this.variableValues = restValues;
			this.variableOptions = restOptions;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
