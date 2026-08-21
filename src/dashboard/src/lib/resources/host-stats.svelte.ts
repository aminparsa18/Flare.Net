// Reactive state for the Resources page's Host overview panel - independent stream from
// `ResourcesState` (no shared enablement, no shared connection), same connect()/dispose()
// lifecycle shape.

import {
	fetchHostStatsSnapshot,
	connectHostStatsWatch,
	type HostStatsSnapshot,
	type HostStatsWatchConnection,
	type HostStatsWatchStatus
} from '$lib/api';

export class HostStatsState {
	snapshot = $state<HostStatsSnapshot | null>(null);
	connectionStatus = $state<HostStatsWatchStatus>('closed');
	error = $state<string | null>(null);

	#connection: HostStatsWatchConnection | null = null;

	/**
	 * Fetches an immediate REST snapshot (so the panel isn't blank waiting on the first
	 * WebSocket push) and opens the watch connection right after, for live updates from
	 * then on. Errors from the initial fetch are non-fatal - the watch connection still
	 * gets a chance to succeed and clear them.
	 */
	async connect(): Promise<void> {
		try {
			this.snapshot = await fetchHostStatsSnapshot();
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}

		this.#connection = connectHostStatsWatch({
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
