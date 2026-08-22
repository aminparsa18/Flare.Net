// Reactive state for the Resources page's Host overview panel - independent stream from
// `ResourcesState` (no shared enablement, no shared connection), same connect()/dispose()
// lifecycle shape.

import {
	fetchHostStatsSnapshot,
	fetchHostStatsHistory,
	connectHostStatsWatch,
	type HostStatsSnapshot,
	type HostStatsHistoryPoint,
	type HostStatsWatchConnection,
	type HostStatsWatchStatus
} from '$lib/api';

// Matches HostStatsOptions.HistoryWindow's own default - the server already trims to this
// window (see HostStatsPoller.AppendHistory), this is a second, client-side trim so memory
// stays bounded if a tab is left open past an hour, not a source of truth for the window
// itself (the backfill fetch below is that).
const HISTORY_WINDOW_MS = 60 * 60 * 1000;

/** Percent-from-bytes, same math the Host overview tiles (HostOverview.svelte) already do - kept here too since a live watch tick only carries used/total bytes, not a precomputed percent (see HostStatsSnapshot.cs's own remarks on why HostStatsHistoryPoint does carry one). */
function percentUsed(used: number, total: number): number {
	return total > 0 ? (100 * used) / total : 0;
}

function historyPointFrom(snapshot: HostStatsSnapshot): HostStatsHistoryPoint | null {
	if (!snapshot.available || !snapshot.updatedAt) return null;
	return {
		timestamp: snapshot.updatedAt,
		cpuUsagePercent: snapshot.cpuUsagePercent,
		memoryUsedPercent: percentUsed(snapshot.memoryUsedBytes, snapshot.memoryTotalBytes),
		diskUsedPercent: percentUsed(snapshot.diskUsedBytes, snapshot.diskTotalBytes),
		networkBytesPerSecond: snapshot.networkBytesPerSecond
	};
}

export class HostStatsState {
	snapshot = $state<HostStatsSnapshot | null>(null);
	/** Oldest first - the Resource trends chart's data source (HostTrendChart.svelte). Seeded from the server's backfill, then appended to as each live watch tick arrives. */
	history = $state<HostStatsHistoryPoint[]>([]);
	connectionStatus = $state<HostStatsWatchStatus>('closed');
	error = $state<string | null>(null);

	#connection: HostStatsWatchConnection | null = null;

	/**
	 * Fetches an immediate REST snapshot and the trend chart's history backfill (so neither
	 * panel is blank waiting on the first WebSocket push), then opens the watch connection
	 * right after, for live updates from then on. Errors from either initial fetch are
	 * non-fatal - the watch connection still gets a chance to succeed and clear them.
	 */
	async connect(): Promise<void> {
		try {
			const [snapshot, history] = await Promise.all([fetchHostStatsSnapshot(), fetchHostStatsHistory()]);
			this.snapshot = snapshot;
			this.history = history;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		}

		this.#connection = connectHostStatsWatch({
			onStatusChange: (status) => (this.connectionStatus = status),
			onSnapshot: (snapshot) => {
				this.snapshot = snapshot;
				this.error = null;

				const point = historyPointFrom(snapshot);
				if (!point) return;
				const cutoff = new Date(point.timestamp).getTime() - HISTORY_WINDOW_MS;
				this.history = [...this.history.filter((p) => new Date(p.timestamp).getTime() > cutoff), point];
			}
		});
	}

	dispose(): void {
		this.#connection?.close();
		this.#connection = null;
	}
}
