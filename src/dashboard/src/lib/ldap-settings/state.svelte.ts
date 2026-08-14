// Central reactive state for the /auth page's Active Directory section - mirrors
// EntraSettingsState's shape ($lib/entra-settings/state.svelte.ts).

import { getLdapSettings, saveLdapSettings, type LdapSettings, type SaveLdapSettingsRequest } from '$lib/ldap-settings-api';

export class LdapSettingsState {
	settings = $state.raw<LdapSettings | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	saving = $state(false);
	saveError = $state<string | null>(null);
	/** True right after a successful save - drives the "Saved" banner (no restart
	 * needed, unlike Entra - see LdapAuthEndpoints' remarks). Reset on the next save
	 * attempt. */
	justSaved = $state(false);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.settings = await getLdapSettings();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	async save(request: SaveLdapSettingsRequest): Promise<boolean> {
		this.saving = true;
		this.saveError = null;
		this.justSaved = false;
		try {
			this.settings = await saveLdapSettings(request);
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
