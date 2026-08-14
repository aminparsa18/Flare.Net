// Central reactive state for the /auth page's OpenID Connect card - mirrors
// EntraSettingsState's shape ($lib/entra-settings/state.svelte.ts): a class with $state
// fields, provided via oidcSettingsContext (context.ts) rather than passed as props.

import { getOidcSettings, saveOidcSettings, type OidcSettings, type SaveOidcSettingsRequest } from '$lib/oidc-settings-api';

export class OidcSettingsState {
	settings = $state.raw<OidcSettings | null>(null);
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
			this.settings = await getOidcSettings();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	async save(request: SaveOidcSettingsRequest): Promise<boolean> {
		this.saving = true;
		this.saveError = null;
		this.justSaved = false;
		try {
			this.settings = await saveOidcSettings(request);
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
