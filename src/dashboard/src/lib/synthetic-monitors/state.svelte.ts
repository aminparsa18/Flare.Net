// Reactive state for synthetic monitors (Settings > Workspace) - same shape as OnCallRotationsState.

import {
	listSyntheticMonitors,
	createSyntheticMonitor,
	updateSyntheticMonitor,
	deleteSyntheticMonitor,
	type SyntheticMonitor,
	type SyntheticMonitorRequest
} from '$lib/synthetic-monitors-api';

export class SyntheticMonitorsState {
	monitors = $state.raw<SyntheticMonitor[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** `null` closed, `'new'` creating, a `SyntheticMonitor` editing it. */
	formTarget = $state<SyntheticMonitor | 'new' | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.monitors = await listSyntheticMonitors();
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

	openEdit(monitor: SyntheticMonitor): void {
		this.saveError = null;
		this.formTarget = monitor;
	}

	closeForm(): void {
		this.formTarget = null;
	}

	async save(request: SyntheticMonitorRequest): Promise<void> {
		const target = this.formTarget;
		this.saving = true;
		this.saveError = null;
		try {
			if (target && target !== 'new') {
				await updateSyntheticMonitor(target.id, request);
			} else {
				await createSyntheticMonitor(request);
			}
			this.formTarget = null;
			await this.load();
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	/** Pause or resume a monitor without opening the form. */
	async setEnabled(monitor: SyntheticMonitor, enabled: boolean): Promise<void> {
		try {
			await updateSyntheticMonitor(monitor.id, {
				name: monitor.name,
				description: monitor.description,
				enabled,
				kind: monitor.kind,
				target: monitor.target,
				method: monitor.method,
				expectedStatus: monitor.expectedStatus,
				requestHeaders: monitor.requestHeaders,
				requestBody: monitor.requestBody,
				bodyContains: monitor.bodyContains,
				bodyNotContains: monitor.bodyNotContains,
				intervalSeconds: monitor.intervalSeconds,
				timeoutSeconds: monitor.timeoutSeconds,
				locations: monitor.locations ?? []
			});
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async remove(id: string): Promise<void> {
		try {
			await deleteSyntheticMonitor(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
