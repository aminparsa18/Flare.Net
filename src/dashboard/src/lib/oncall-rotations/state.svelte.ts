// Reactive state for on-call rotations (Settings > Workspace, and the alert rule form's
// rotation picker) - same shape as MaintenanceWindowsState.

import {
	listOnCallRotations,
	createOnCallRotation,
	updateOnCallRotation,
	deleteOnCallRotation,
	type OnCallRotation,
	type OnCallRotationRequest,
	type OnCallRotationStatus
} from '$lib/oncall-rotations-api';

export class OnCallRotationsState {
	rotations = $state.raw<OnCallRotationStatus[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** `null` closed, `'new'` creating, a `OnCallRotation` editing it. */
	formTarget = $state<OnCallRotation | 'new' | null>(null);
	saving = $state(false);
	saveError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.rotations = await listOnCallRotations();
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

	openEdit(rotation: OnCallRotation): void {
		this.saveError = null;
		this.formTarget = rotation;
	}

	closeForm(): void {
		this.formTarget = null;
	}

	async save(request: OnCallRotationRequest): Promise<void> {
		const target = this.formTarget;
		this.saving = true;
		this.saveError = null;
		try {
			if (target && target !== 'new') {
				await updateOnCallRotation(target.id, request);
			} else {
				await createOnCallRotation(request);
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
			await deleteOnCallRotation(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
