// Central reactive state for the Resources page - one instance per page, provided via
// `resourcesContext` (context.ts) rather than passed as props, same shape as
// `LogsExplorerState`'s connection lifecycle.

import {
	fetchResourceSnapshot,
	connectResourcesWatch,
	type ResourceGraphSnapshot,
	type ResourcesWatchConnection,
	type ResourcesWatchStatus
} from '$lib/api';

export class ResourcesState {
	snapshot = $state<ResourceGraphSnapshot | null>(null);
	connectionStatus = $state<ResourcesWatchStatus>('closed');
	error = $state<string | null>(null);

	#connection: ResourcesWatchConnection | null = null;

	/**
	 * Fetches an immediate REST snapshot (so the page isn't blank waiting on the first
	 * WebSocket push) and opens the watch connection right after, for live updates from
	 * then on. Errors from the initial fetch are non-fatal - the watch connection still
	 * gets a chance to succeed and clear them.
	 */
	async connect(): Promise<void> {
		try {
			this.snapshot = await fetchResourceSnapshot();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}

		this.#connection = connectResourcesWatch({
			onStatusChange: (status) => (this.connectionStatus = status),
			onSnapshot: (snapshot) => {
				this.snapshot = snapshot;
				this.error = null;
			}
		});
	}

	dispose(): void {
		this.#connection?.close();
		this.#connection = null;
	}
}
