// Central reactive auth state - the root layout creates one instance via
// authContext.set(new AuthState()) and calls .initialize() once on mount; every
// descendant (the route guard in +layout.svelte itself, AppNav's user/logout UI, the
// login/setup pages) reads it via authContext.get() instead of receiving it as a prop.

import {
	bootstrap as bootstrapRequest,
	getBootstrapStatus,
	getCurrentUser,
	login as loginRequest,
	loginLdap as loginLdapRequest,
	loginViaProxy as loginViaProxyRequest,
	logout as logoutRequest,
	type AuthUser
} from '$lib/auth-api';

export class AuthState {
	currentUser = $state.raw<AuthUser | null>(null);

	/** The opt-in-auth global switch (see docs/auth.md) - defaults to `true` while
	 * unknown (pre-{@link initialize}), the same fail-toward-secure bias
	 * `IAuthSettingsStore.GetAsync`'s own defensive fallback uses, so nothing ever
	 * flashes "open" for a frame before the real value is known. */
	authEnabled = $state(true);

	/** True until the first {@link initialize} call resolves - the route guard waits on
	 * this specifically (not {@link loading}, which also flips for login/logout/bootstrap
	 * calls made afterward) before deciding where to send an unauthenticated visitor, so
	 * it doesn't redirect to /login for one frame before a valid session cookie is found. */
	initializing = $state(true);

	/** True while a login/bootstrap/logout call is in flight - the login/setup forms use
	 * this to disable their submit button, same shape as IndexingState.loading. */
	loading = $state(false);

	error = $state<string | null>(null);

	/** Learns whether auth is even required at all, then - only if it is - checks for an
	 * existing session cookie. Never throws - a failed check just leaves currentUser
	 * null and authEnabled at its fail-secure default, same as no session existing (an
	 * unreachable API is indistinguishable from "not logged in" for routing purposes;
	 * getCurrentUser's own network-error case would already have surfaced elsewhere). */
	async initialize(): Promise<void> {
		try {
			const status = await getBootstrapStatus();
			this.authEnabled = status.authEnabled;
			if (status.authEnabled) {
				this.currentUser = await getCurrentUser();
			}
		} catch {
			this.currentUser = null;
		} finally {
			this.initializing = false;
		}
	}

	async login(username: string, password: string): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.currentUser = await loginRequest(username, password);
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	async loginLdap(username: string, password: string): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.currentUser = await loginLdapRequest(username, password);
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	/** `POST /api/auth/proxy/login` - unlike {@link login}/{@link loginLdap}, called with
	 * no credentials, no user action, automatically by `/login` when the reverse-proxy
	 * method is enabled (see ProxyAuthLoginEndpoints). A failure (untrusted network,
	 * missing header, disabled account) leaves `error` set exactly like a rejected
	 * password would - the login page falls back to whatever other methods are enabled
	 * rather than getting stuck. */
	async loginViaProxy(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.currentUser = await loginViaProxyRequest();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	async bootstrap(username: string, password: string): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.currentUser = await bootstrapRequest(username, password);
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	/** Clears the local session, then - only for a ReverseProxy-provisioned account with
	 * a configured logout redirect URL (see docs/auth.md's "Known limitations") - sends
	 * the whole browser there via a real navigation, not client-side routing, since it's
	 * typically a different origin (the proxy's or IdP's own sign-out endpoint). Every
	 * other case falls through to AppNav.svelte's usual "currentUser went null" ->
	 * goto('/login') reaction, unchanged. */
	async logout(): Promise<void> {
		this.loading = true;
		let redirectUrl: string | null = null;
		try {
			redirectUrl = (await logoutRequest()).redirectUrl;
		} finally {
			this.currentUser = null;
			this.loading = false;
		}
		if (redirectUrl) {
			window.location.href = redirectUrl;
		}
	}
}
