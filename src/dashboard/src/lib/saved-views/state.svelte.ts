// Central reactive state for the Views management page - mirrors AlertsState's shape
// (`$lib/alerts/state.svelte.ts`): a class with `$state` fields, provided via
// `savedViewsContext` rather than passed as props. Narrower than AlertsState: there's no
// "create" flow here (a saved view is always created from a source explorer page's
// current state - see LogsToolbar/TracesToolbar/MetricsToolbar's own "Save current
// view..." action - not from a blank form on this page), so only rename + delete apply.

import { listSavedViews, deleteSavedView, updateSavedView, type SavedView } from '$lib/saved-views-api';

export class SavedViewsState {
	views = $state.raw<SavedView[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** Drives the rename dialog - `null` closed, else the view being renamed. Mirrors `AlertsState.formTarget`'s pattern, narrowed to rename-only (no 'new' case here). */
	renameTarget = $state<SavedView | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			const res = await listSavedViews();
			this.views = res.views;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	openRename(view: SavedView): void {
		this.saveError = null;
		this.renameTarget = view;
	}

	closeRename(): void {
		this.renameTarget = null;
	}

	/** Renames/redescribes a view - its `pageType`/`state` (the filter payload) pass through unchanged, since this dialog only edits name+description. */
	async rename(name: string, description: string): Promise<void> {
		const target = this.renameTarget;
		if (!target) return;
		this.saving = true;
		this.saveError = null;
		try {
			await updateSavedView(target.id, {
				name,
				description,
				pageType: target.pageType,
				state: target.state
			});
			this.renameTarget = null;
			await this.load();
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async remove(id: string): Promise<void> {
		try {
			await deleteSavedView(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
