// Central reactive state for the Indexing page - single no-argument query (unlike
// Ingestion's windowed one), since table/index storage doesn't need a time-window
// selector - it's "what does my ClickHouse instance look like right now." No polling
// either, unlike IngestionState - schema/storage shape doesn't change on a 10s cadence
// the way live throughput does; a manual refresh button covers the "I just generated
// traffic and want to see it reflected" case instead.

import { getIndexingStats, getClusterStatus, type IndexingStatsResponse, type ClusterStatusResponse } from '$lib/indexing-api';

export class IndexingState {
	stats = $state.raw<IndexingStatsResponse | null>(null);
	/**
	 * Fetched alongside `stats` on every `load()`, not gated behind it - a single-node
	 * deployment gets `{ clusterModeEnabled: false, nodes: [] }` back cheaply (see
	 * ClusterQueryService's remarks), so there's no need to skip this call client-side
	 * first; IndexingClusterStatus.svelte just renders nothing when it's false.
	 */
	clusterStatus = $state.raw<ClusterStatusResponse | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	#abort: AbortController | null = null;

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		this.loading = true;
		this.error = null;
		try {
			const [stats, clusterStatus] = await Promise.all([getIndexingStats(abort.signal), getClusterStatus(abort.signal)]);
			if (abort.signal.aborted) return;
			this.stats = stats;
			this.clusterStatus = clusterStatus;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	dispose(): void {
		this.#abort?.abort();
	}
}
