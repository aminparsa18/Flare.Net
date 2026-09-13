// Reactive state for one dashboard's viewer/editor page (routes/dashboards/[id]) -
// narrower than DashboardsState (the list page): loads exactly one dashboard by id.
// Phase 1 (see docs-internal/adr/0023-custom-dashboards.md) only ever mutated a panel
// list by removing one; Phase 2 (docs-internal/adr/0024-custom-dashboards-phase2-editor.md)
// adds `editing` (drives DashboardGrid.svelte's drag/resize), a dashboard-wide time-range
// override, and add/reposition/rename alongside remove. Phase 3 (roadmap's "Custom,
// user-built dashboards" item) adds an auto-refresh interval and the "set as home" toggle.

import { getDashboard, updateDashboard, type DashboardSummary, type DashboardPanel } from '$lib/dashboards-api';
import type { TimeRangePreset } from '$lib/logs/time-range';
import { type RefreshInterval, refreshIntervalMs } from './refresh-intervals';
import { getHomeDashboardId, setHomeDashboardId, clearHomeDashboardIdIfMatching } from './home-preference';

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
	}

	setEditing(v: boolean): void {
		this.editing = v;
	}

	setTimeRangeOverride(preset: TimeRangePreset | null): void {
		this.timeRangeOverride = preset;
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
}
