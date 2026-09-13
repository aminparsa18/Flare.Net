// Reactive state for one dashboard's viewer/editor page (routes/dashboards/[id]) -
// narrower than DashboardsState (the list page): loads exactly one dashboard by id.
// Phase 1 (see docs-internal/adr/0023-custom-dashboards.md) only ever mutated a panel
// list by removing one; Phase 2 (docs-internal/adr/0024-custom-dashboards-phase2-editor.md)
// adds `editing` (drives DashboardGrid.svelte's drag/resize), a dashboard-wide time-range
// override, and add/reposition/rename alongside remove. Phase 3 (roadmap's "Custom,
// user-built dashboards" item) adds an auto-refresh interval and the "set as home" toggle.
// Phase 4 adds a dashboard-wide "Service" override - the scoped-down MVP of dashboard
// variables (see roadmap.md's own remarks on what's deliberately *not* built here):
// session-only, one fixed built-in variable, no query-backed/chained variables.
//
// duplicatePanel()/exportPanel() (roadmap follow-up to Phase 3, which only covered a whole
// dashboard) are this class's own counterparts to DashboardsState's duplicate()/
// exportDashboard() - same "just another mutation against what's already loaded, no new
// API endpoint" shape, just scoped to one panel within `dashboard` instead of a whole
// dashboard in the list.

import { getDashboard, updateDashboard, type DashboardSummary, type DashboardPanel } from '$lib/dashboards-api';
import { aggregateLogs } from '$lib/api';
import type { TimeRangePreset } from '$lib/logs/time-range';
import { type RefreshInterval, refreshIntervalMs } from './refresh-intervals';
import { getHomeDashboardId, setHomeDashboardId, clearHomeDashboardIdIfMatching } from './home-preference';
import { nextPanelPosition } from './layout';
import { slugify } from './state.svelte';
import { downloadBlob } from '$lib/logs/export';
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
	 * Overrides every panel's own `services` filter for this session, or `null` to leave
	 * each panel showing whatever service(s) it was pinned/added with - same session-only
	 * reasoning as timeRangeOverride above (this is the MVP "variable": one fixed built-in,
	 * not a saved/query-backed one - see roadmap.md's own remarks on the fuller version
	 * left as still-open work).
	 */
	serviceOverride = $state<string | null>(null);

	/** Options for the Service override picker - loaded once in load() below, same wide-window
	 *  aggregate `LogsExplorerState.loadKnownServices`/AlertRuleFormDialog's own copy of it use
	 *  (see this class's loadKnownServices' own remarks for why this is a third copy rather
	 *  than sharing one of those). */
	knownServices = $state<string[]>([]);

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
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
			clearHomeDashboardIdIfMatching(id);
		} finally {
			this.loading = false;
		}
		// Fire-and-forget, same as AlertRuleFormDialog's own onMount - the service picker
		// just shows fewer/no options until this resolves, not worth blocking the dashboard
		// itself (`loading` above) on.
		void this.loadKnownServices();
	}

	/** One-off, wide-window (7d) aggregate to enumerate service names - same query
	 *  `LogsExplorerState.loadKnownServices`/AlertRuleFormDialog's own copy run, duplicated
	 *  here rather than shared for the same reason AlertRuleFormDialog's own copy gives:
	 *  this page has no LogsExplorerState instance of its own to borrow one from (each
	 *  panel's own private explorer, inside DashboardLogsPanelBody, is scoped to that one
	 *  panel and may not even be a Logs panel). */
	async loadKnownServices(): Promise<void> {
		try {
			const to = new Date();
			const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
			const res = await aggregateLogs({
				filter: { from: from.toISOString(), to: to.toISOString() },
				bucketWidthSeconds: 7 * 24 * 60 * 60,
				groupBy: 'Service'
			});
			this.knownServices = [...new Set(res.buckets.map((b) => b.groupKey).filter((k): k is string => !!k))].sort();
		} catch {
			// Non-critical - the picker just shows fewer/no options until a retry.
		}
	}

	setEditing(v: boolean): void {
		this.editing = v;
	}

	setTimeRangeOverride(preset: TimeRangePreset | null): void {
		this.timeRangeOverride = preset;
	}

	setServiceOverride(service: string | null): void {
		this.serviceOverride = service;
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
		this.dashboard = { ...dashboard, layout: { panels } };
		try {
			await updateDashboard(dashboard.id, { name: dashboard.name, description: dashboard.description, layout: { panels } });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async addPanel(panel: DashboardPanel): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		try {
			const layout = { panels: [...dashboard.layout.panels, panel] };
			this.dashboard = await updateDashboard(dashboard.id, { name: dashboard.name, description: dashboard.description, layout });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async renamePanel(panelId: string, title: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		try {
			const layout = { panels: dashboard.layout.panels.map((p) => (p.id === panelId ? { ...p, title } : p)) };
			this.dashboard = await updateDashboard(dashboard.id, { name: dashboard.name, description: dashboard.description, layout });
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async removePanel(panelId: string): Promise<void> {
		const dashboard = this.dashboard;
		if (!dashboard) return;
		this.removingPanelId = panelId;
		try {
			const layout = { panels: dashboard.layout.panels.filter((p) => p.id !== panelId) };
			this.dashboard = await updateDashboard(dashboard.id, {
				name: dashboard.name,
				description: dashboard.description,
				layout
			});
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
}
