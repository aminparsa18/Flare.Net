// Central reactive state for the alerts page's Maintenance tab - same shape as
// NotificationChannelsState (`$lib/notification-channels/state.svelte.ts`), minus send-test.

import {
	listMaintenanceWindows,
	createMaintenanceWindow,
	updateMaintenanceWindow,
	deleteMaintenanceWindow,
	type MaintenanceWindow,
	type MaintenanceWindowRequest
} from '$lib/maintenance-windows-api';

export class MaintenanceWindowsState {
	windows = $state.raw<MaintenanceWindow[]>([]);
	/** Ids of windows active right now, as of the last `load()`. */
	activeWindowIds = $state.raw<string[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** `null` closed, `'new'` creating, a `MaintenanceWindow` editing it. */
	formTarget = $state<MaintenanceWindow | 'new' | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	/** True when an active window silences `ruleId` - drives the rules table's "muted" badge. */
	isRuleMuted(ruleId: string): boolean {
		return this.windows.some((w) => this.activeWindowIds.includes(w.id) && (w.ruleIds.length === 0 || w.ruleIds.includes(ruleId)));
	}

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			const res = await listMaintenanceWindows();
			this.windows = res.windows;
			this.activeWindowIds = res.activeWindowIds;
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

	openEdit(window: MaintenanceWindow): void {
		this.saveError = null;
		this.formTarget = window;
	}

	closeForm(): void {
		this.formTarget = null;
	}

	async save(request: MaintenanceWindowRequest): Promise<void> {
		const target = this.formTarget;
		this.saving = true;
		this.saveError = null;
		try {
			if (target && target !== 'new') {
				await updateMaintenanceWindow(target.id, request);
			} else {
				await createMaintenanceWindow(request);
			}
			this.formTarget = null;
			await this.load();
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async remove(id: string): Promise<void> {
		try {
			await deleteMaintenanceWindow(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
