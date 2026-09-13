// Central reactive state for the Dashboards management page - mirrors SavedViewsState's
// shape (`$lib/saved-views/state.svelte.ts`), widened to also cover creation: unlike a
// SavedView (always born from a source explorer page's current filter), a dashboard is
// created blank from this page and filled in afterward by pinning panels to it from the
// Logs/Traces/Metrics toolbars (see PinToDashboardDialog.svelte) - so `formTarget` follows
// AlertsState's 'new' | existing-target pattern instead of SavedViewsState's rename-only one.
//
// Phase 3 (roadmap's "Custom, user-built dashboards" item) adds duplicate() and export():
// both are pure client-side operations against a dashboard already in `dashboards` - no new
// API endpoint, since a duplicate is just another createDashboard() call and an export is
// just that same request body written to a file (see Dashboard's C# remarks: LayoutJson
// only ever holds panel definitions, never cached query results, so there's nothing to
// strip before writing it out).

import { listDashboards, createDashboard, updateDashboard, deleteDashboard, type DashboardSummary } from '$lib/dashboards-api';
import { downloadBlob } from '$lib/logs/export';
import * as m from '$lib/paraglide/messages';

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

	/** Creates a copy of `dashboard` with all the same panels. Returns the copy's id (so the
	 *  caller can navigate straight to it) or `null` on failure. */
	async duplicate(dashboard: DashboardSummary): Promise<string | null> {
		try {
			const copy = await createDashboard({
				name: m.dashboardTable_duplicateName({ name: dashboard.name }),
				description: dashboard.description,
				layout: dashboard.layout
			});
			await this.load();
			return copy.id;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
			return null;
		}
	}

	/** Downloads `dashboard`'s definition (name/description/panels - never any cached query
	 *  result, since none is ever stored) as a JSON file a later "New dashboard" + this same
	 *  file couldn't yet re-import (no import path exists), but stands on its own as a
	 *  human-readable backup/diff-able snapshot in the meantime. */
	exportDashboard(dashboard: DashboardSummary): void {
		const body = { name: dashboard.name, description: dashboard.description, layout: dashboard.layout };
		const blob = new Blob([JSON.stringify(body, null, 2)], { type: 'application/json;charset=utf-8' });
		downloadBlob(blob, `flare-dashboard_${slugify(dashboard.name)}.json`);
	}
}

/** Lowercases and replaces anything that isn't a letter/digit with `-`, collapsing runs -
 *  same rough shape as exportFilename's own timestamp sanitizing in `$lib/logs/export.ts`,
 *  just for a name instead of a timestamp. Falls back to "dashboard" for an all-punctuation
 *  name so the filename is never just `flare-dashboard_.json`. */
function slugify(name: string): string {
	const slug = name
		.toLowerCase()
		.replace(/[^a-z0-9]+/g, '-')
		.replace(/^-+|-+$/g, '');
	return slug || 'dashboard';
}
