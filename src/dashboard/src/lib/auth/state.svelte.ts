// Central reactive auth state - the root layout creates one instance via
// authContext.set(new AuthState()) and calls .initialize() once on mount; every
// descendant (the route guard in +layout.svelte itself, AppNav's user/logout UI, the
// login/setup pages) reads it via authContext.get() instead of receiving it as a prop.

import { bootstrap as bootstrapRequest, getCurrentUser, login as loginRequest, logout as logoutRequest, type AuthUser } from '$lib/auth-api';

export class AuthState {
	currentUser = $state.raw<AuthUser | null>(null);

	/** True until the first {@link initialize} call resolves - the route guard waits on
	 * this specifically (not {@link loading}, which also flips for login/logout/bootstrap
	 * calls made afterward) before deciding where to send an unauthenticated visitor, so
	 * it doesn't redirect to /login for one frame before a valid session cookie is found. */
	initializing = $state(true);

	/** True while a login/bootstrap/logout call is in flight - the login/setup forms use
	 * this to disable their submit button, same shape as IndexingState.loading. */
	loading = $state(false);

	error = $state<string | null>(null);

	/** Checks for an existing session cookie on app load. Never throws - a failed check
	 * just leaves currentUser null, same as no session existing (an unreachable API is
	 * indistinguishable from "not logged in" for routing purposes; getCurrentUser's own
	 * network-error case would already have surfaced elsewhere). */
	async initialize(): Promise<void> {
		try {
			this.currentUser = await getCurrentUser();
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

	async logout(): Promise<void> {
		this.loading = true;
		try {
			await logoutRequest();
		} finally {
			this.currentUser = null;
			this.loading = false;
		}
	}
}
