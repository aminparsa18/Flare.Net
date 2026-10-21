// Reactive state for cron and job check-in monitors (Settings > Workspace) - same shape as SyntheticMonitorsState.

import {
	listCheckInMonitors,
	createCheckInMonitor,
	updateCheckInMonitor,
	deleteCheckInMonitor,
	listCheckInRuns,
	requestFrom,
	type CheckInMonitor,
	type CheckInRun,
	type CheckInMonitorRequest
} from '#lib/checkin-monitors-api.ts';

export class CheckInMonitorsState {
	monitors = $state.raw<CheckInMonitor[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** `null` closed, `'new'` creating, a `CheckInMonitor` editing it. */
	formTarget = $state<CheckInMonitor | 'new' | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	/** The monitor whose run history dialog is open, or `null`. */
	runsTarget = $state<CheckInMonitor | null>(null);
	runs = $state.raw<CheckInRun[]>([]);
	runsLoading = $state(false);
	runsError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.monitors = await listCheckInMonitors();
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

	openEdit(monitor: CheckInMonitor): void {
		this.saveError = null;
		this.formTarget = monitor;
	}

	async openRuns(monitor: CheckInMonitor): Promise<void> {
		this.runsTarget = monitor;
		this.runs = [];
		this.runsError = null;
		this.runsLoading = true;
		try {
			const runs = await listCheckInRuns(monitor.id);
			// Ignore a response for a dialog that was closed or reopened on another monitor meanwhile.
			if (this.runsTarget?.id === monitor.id) this.runs = runs;
		} catch (err) {
			if (this.runsTarget?.id === monitor.id) this.runsError = err instanceof Error ? err.message : String(err);
		} finally {
			if (this.runsTarget?.id === monitor.id) this.runsLoading = false;
		}
	}

	closeRuns(): void {
		this.runsTarget = null;
	}

	closeForm(): void {
		this.formTarget = null;
	}

	async save(request: CheckInMonitorRequest): Promise<void> {
		const target = this.formTarget;
		this.saving = true;
		this.saveError = null;
		try {
			if (target && target !== 'new') {
				await updateCheckInMonitor(target.id, request);
			} else {
				await createCheckInMonitor(request);
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
	async setEnabled(monitor: CheckInMonitor, enabled: boolean): Promise<void> {
		try {
			await updateCheckInMonitor(monitor.id, requestFrom(monitor, enabled));
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	async remove(id: string): Promise<void> {
		try {
			await deleteCheckInMonitor(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
