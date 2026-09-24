// Central reactive state for the Ingest Keys page - same shape as AccessTokensState
// (`$lib/access-tokens/state.svelte.ts`: create with a reveal-once raw value, revoke),
// plus one extra mutation: editing a key's ingestion limits (ADR-0051), the only part of
// an ingest key that isn't fixed at creation.

import {
	createIngestApiKey,
	listIngestApiKeys,
	revokeIngestApiKey,
	updateIngestApiKeyLimits,
	type IngestApiKeyDto,
	type IngestApiKeyLimits
} from '$lib/ingest-keys-api';

export class IngestKeysState {
	keys = $state.raw<IngestApiKeyDto[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	createOpen = $state(false);
	saving = $state(false);
	saveError = $state<string | null>(null);

	/** The just-created key's raw value, shown exactly once by the create dialog - cleared on close. */
	revealedKey = $state<string | null>(null);

	/** The key whose limits dialog is open, or null when closed. */
	limitsTarget = $state<IngestApiKeyDto | null>(null);

	/** `silent` skips the spinner - used by the page's usage auto-refresh so the table
	 *  doesn't flash every few seconds. */
	async load(silent = false): Promise<void> {
		if (!silent) this.loading = true;
		this.error = null;
		try {
			this.keys = await listIngestApiKeys();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loading = false;
		}
	}

	openCreate(): void {
		this.saveError = null;
		this.revealedKey = null;
		this.createOpen = true;
	}

	closeCreate(): void {
		this.createOpen = false;
		this.revealedKey = null;
	}

	async create(name: string): Promise<void> {
		this.saving = true;
		this.saveError = null;
		try {
			const response = await createIngestApiKey({ name });
			this.revealedKey = response.rawKey;
			await this.load(true);
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}

	async revoke(id: string): Promise<void> {
		try {
			await revokeIngestApiKey(id);
			await this.load(true);
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}
	}

	openLimits(key: IngestApiKeyDto): void {
		this.saveError = null;
		this.limitsTarget = key;
	}

	closeLimits(): void {
		this.limitsTarget = null;
	}

	async saveLimits(limits: IngestApiKeyLimits): Promise<void> {
		if (!this.limitsTarget) return;
		this.saving = true;
		this.saveError = null;
		try {
			await updateIngestApiKeyLimits(this.limitsTarget.id, limits);
			this.limitsTarget = null;
			await this.load(true);
		} catch (err) {
			this.saveError = err instanceof Error ? err.message : String(err);
		} finally {
			this.saving = false;
		}
	}
}
