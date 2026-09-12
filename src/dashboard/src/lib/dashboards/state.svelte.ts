// Central reactive state for the Dashboards management page - mirrors SavedViewsState's
// shape (`$lib/saved-views/state.svelte.ts`), widened to also cover creation: unlike a
// SavedView (always born from a source explorer page's current filter), a dashboard is
// created blank from this page and filled in afterward by pinning panels to it from the
// Logs/Traces/Metrics toolbars (see PinToDashboardDialog.svelte) - so `formTarget` follows
// AlertsState's 'new' | existing-target pattern instead of SavedViewsState's rename-only one.

import { listDashboards, createDashboard, updateDashboard, deleteDashboard, type DashboardSummary } from '$lib/dashboards-api';

export class DashboardsState {
	dashboards = $state.raw<DashboardSummary[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** Drives the create/rename dialog - `null` closed, `'new'` the blank-create form, else the dashboard being renamed. */
	formTarget = $state<DashboardSummary | 'new' | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			const res = await listDashboards();
			this.dashboards = res.dashboards;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	openCreate(): void {
		this.saveError = null;
		this.formTarget = 'new';
	}

	openRename(dashboard: DashboardSummary): void {
		this.saveError = null;
		this.formTarget = dashboard;
	}

	closeForm(): void {
		this.formTarget = null;
	}

	/** Returns the created dashboard's id (so the caller can navigate straight to its viewer) or `null` on failure. */
	async save(name: string, description: string): Promise<string | null> {
		const target = this.formTarget;
		if (!target) return null;
		this.saving = true;
		this.saveError = null;
		try {
			if (target === 'new') {
				const created = await createDashboard({ name, description, layout: { panels: [] } });
				this.formTarget = null;
				await this.load();
				return created.id;
			} else {
				await updateDashboard(target.id, { name, description, layout: target.layout });
				this.formTarget = null;
				await this.load();
				return target.id;
			}
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
			return null;
		} finally {
			this.saving = false;
		}
	}

	async remove(id: string): Promise<void> {
		try {
			await deleteDashboard(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
