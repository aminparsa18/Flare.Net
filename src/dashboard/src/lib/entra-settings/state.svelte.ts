// Central reactive state for the Security page - mirrors UsersState's shape
// ($lib/users/state.svelte.ts): a class with $state fields, provided via
// entraSettingsContext (context.ts) rather than passed as props.

import { getEntraSettings, saveEntraSettings, type EntraSettings, type SaveEntraSettingsRequest } from '$lib/entra-settings-api';

export class EntraSettingsState {
	settings = $state.raw<EntraSettings | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	saving = $state(false);
	saveError = $state<string | null>(null);
	/** True right after a successful save - drives the "Saved - restart Flare.Api to
	 * apply" banner. Reset on the next save attempt, not on every keystroke - the point
	 * is "did my last save succeed," not "is the form dirty." */
	justSaved = $state(false);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.settings = await getEntraSettings();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	async save(request: SaveEntraSettingsRequest): Promise<boolean> {
		this.saving = true;
		this.saveError = null;
		this.justSaved = false;
		try {
			this.settings = await saveEntraSettings(request);
			this.justSaved = true;
			return true;
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
			return false;
		} finally {
			this.saving = false;
		}
	}
}
