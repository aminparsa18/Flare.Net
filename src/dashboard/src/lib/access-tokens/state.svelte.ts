// Central reactive state for the Access Tokens page - mirrors SavedViewsState's shape
// (`$lib/saved-views/state.svelte.ts`): a class with `$state` fields, provided via
// accessTokensContext rather than passed as props. Narrower than AlertsState: there's no
// "edit" flow (a token is immutable once created - only name/expiry are ever set, and
// only at creation), so create + revoke are the only mutations, same as ingest API keys'
// own create/revoke-only shape (PersonalAccessTokenEndpoints.cs).

import { createAccessToken, listAccessTokens, revokeAccessToken, type AccessToken } from '$lib/personal-access-tokens-api';

export class AccessTokensState {
	tokens = $state.raw<AccessToken[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	/** Drives the create dialog - false closed, true open. Unlike AlertsState.formTarget
	 * there's no "editing X" case to distinguish, so a plain boolean is enough. */
	createOpen = $state(false);
	saving = $state(false);
	saveError = $state<string | null>(null);

	/** The just-created token's raw value, shown exactly once by the create dialog's
	 * "reveal" step - cleared on dialog close (`closeCreate`). Flare never stores or
	 * displays this again after this, same as `CreateAccessTokenResponse.RawToken`'s own
	 * doc comment on the backend. */
	revealedToken = $state<string | null>(null);

	async load(): Promise<void> {
		this.loading = true;
		this.error = null;
		try {
			this.tokens = await listAccessTokens();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	openCreate(): void {
		this.saveError = null;
		this.revealedToken = null;
		this.createOpen = true;
	}

	closeCreate(): void {
		this.createOpen = false;
		this.revealedToken = null;
	}

	async create(name: string, expiresInDays: number | null): Promise<void> {
		this.saving = true;
		this.saveError = null;
		try {
			const response = await createAccessToken({ name, expiresInDays });
			this.revealedToken = response.rawToken;
			await this.load();
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async revoke(id: string): Promise<void> {
		try {
			await revokeAccessToken(id);
			await this.load();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}
}
