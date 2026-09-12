// Reactive state for one dashboard's viewer/editor page (routes/dashboards/[id]) -
// narrower than DashboardsState (the list page): loads exactly one dashboard by id, and
// the only mutation is removing a panel (added via PinToDashboardDialog.svelte from a
// source Explorer page's toolbar, never from here - see docs-internal/adr/0023-custom-dashboards.md).

import { getDashboard, updateDashboard, type DashboardSummary } from '$lib/dashboards-api';

export class DashboardViewerState {
	dashboard = $state<DashboardSummary | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);
	removingPanelId = $state<string | null>(null);

	async load(id: string): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.dashboard = await getDashboard(id);
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
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
