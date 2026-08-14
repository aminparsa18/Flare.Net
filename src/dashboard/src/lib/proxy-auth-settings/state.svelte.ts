// Central reactive state for the /auth page's Reverse proxy card - mirrors
// LdapSettingsState's shape ($lib/ldap-settings/state.svelte.ts): a class with $state
// fields, provided via proxyAuthSettingsContext (context.ts) rather than passed as props.

import {
	getProxyAuthSettings,
	saveProxyAuthSettings,
	type ProxyAuthSettings,
	type SaveProxyAuthSettingsRequest
} from '$lib/proxy-auth-settings-api';

export class ProxyAuthSettingsState {
	settings = $state.raw<ProxyAuthSettings | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	saving = $state(false);
	saveError = $state<string | null>(null);
	/** True right after a successful save - drives the "Saved." banner. Reset on the
	 * next save attempt, not on every keystroke. No "restart Flare.Api" wording -
	 * unlike Entra/OIDC, this method registers no ASP.NET Core authentication scheme,
	 * so a save takes effect on the very next login attempt, same as LDAP's. */
	justSaved = $state(false);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.settings = await getProxyAuthSettings();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	async save(request: SaveProxyAuthSettingsRequest): Promise<boolean> {
		this.saving = true;
		this.saveError = null;
		this.justSaved = false;
		try {
			this.settings = await saveProxyAuthSettings(request);
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
