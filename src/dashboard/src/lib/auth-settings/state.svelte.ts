// Central reactive state for the /auth page's umbrella switch - mirrors
// EntraSettingsState's shape ($lib/entra-settings/state.svelte.ts).

import { getAuthSettings, saveAuthSettings, type AuthSettings } from '$lib/auth-settings-api';

export class AuthSettingsState {
	settings = $state.raw<AuthSettings | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	saving = $state(false);
	saveError = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.settings = await getAuthSettings();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	/** Returns the saved settings on success (null on failure, with {@link saveError}
	 * set) - the caller (AuthToggleCard) is responsible for propagating a successful
	 * `enabled` change into the shared AuthState so +layout.svelte's route guard
	 * re-evaluates immediately, since this class only owns the /auth page's own local
	 * view of the setting. */
	async save(settings: AuthSettings): Promise<AuthSettings | null> {
		this.saving = true;
		this.saveError = null;
		try {
			this.settings = await saveAuthSettings(settings);
			return this.settings;
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
			return null;
		} finally {
			this.saving = false;
		}
	}
}
