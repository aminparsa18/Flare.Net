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
//
// importDashboard() (roadmap follow-up: "importing this app's own dashboard-export JSON
// back in") closes the loop - it's the read side of exportDashboard()'s write, and, like
// duplicate(), still just another createDashboard() call under the hood. No new API
// endpoint here either, and no import of a *foreign* (e.g. Grafana) dashboard JSON - that's
// a separate, larger roadmap item.
//
// The per-panel counterparts (another roadmap follow-up: "Phase 3 only covers a whole
// dashboard") live in DashboardViewerState.duplicatePanel/exportPanel instead of here -
// this class only ever has a dashboard *list* (DashboardSummary, no panels loaded until
// you open one), while a panel-level operation needs the one dashboard already loaded by
// the viewer page. Both share this file's slugify() helper (exported below) for the same
// filename-sanitizing reason.

import { listDashboards, createDashboard, updateDashboard, deleteDashboard, parseLayout, type DashboardSummary } from '$lib/dashboards-api';
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

	/** Set by importDashboard() on a bad file - separate from `error`/`saveError` since it's
	 *  surfaced next to the Import button itself, not inside the create/rename dialog. */
	importError = $state<string | null>(null);

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

	/** The reverse of exportDashboard(): reads `file` (as produced by that method, or hand-
	 *  edited from it), creates a new dashboard from it, and returns the copy's id (so the
	 *  caller can navigate straight to it) or `null` on failure. A malformed `layout` is
	 *  handled the same lenient way parseLayout() already treats a malformed server response
	 *  - dropped to an empty panel list rather than rejected - but a missing/blank `name` is
	 *  rejected outright, since unlike layout there's no sane default for it. */
	async importDashboard(file: File): Promise<string | null> {
		this.importError = null;
		let parsed: unknown;
		try {
			parsed = JSON.parse(await file.text());
		} catch {
			this.importError = m.dashboardTable_importInvalidJson();
			return null;
		}
		if (parsed == null || typeof parsed !== 'object' || typeof (parsed as { name?: unknown }).name !== 'string' || !(parsed as { name: string }).name.trim()) {
			this.importError = m.dashboardTable_importInvalidShape();
			return null;
		}
		const body = parsed as { name: string; description?: unknown; layout?: unknown };
		try {
			const created = await createDashboard({
				name: body.name,
				description: typeof body.description === 'string' ? body.description : undefined,
				layout: parseLayout(body.layout)
			});
			await this.load();
			return created.id;
		} catch (err) {
			this.importError = err instanceof Error ? err.message : String(err);
			return null;
		}
	}
}

/** Lowercases and replaces anything that isn't a letter/digit with `-`, collapsing runs -
 *  same rough shape as exportFilename's own timestamp sanitizing in `$lib/logs/export.ts`,
 *  just for a name instead of a timestamp. Falls back to `fallback` for an all-punctuation
 *  name so the filename is never just `flare-dashboard_.json`. Exported for
 *  DashboardViewerState.exportPanel's own per-panel filename, same reasoning as here. */
export function slugify(name: string, fallback = 'dashboard'): string {
	const slug = name
		.toLowerCase()
		.replace(/[^a-z0-9]+/g, '-')
		.replace(/^-+|-+$/g, '');
	return slug || fallback;
}
