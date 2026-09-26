// Central reactive state for the Indexing page - single no-argument query (unlike
// Ingestion's windowed one), since table/index storage doesn't need a time-window
// selector - it's "what does my ClickHouse instance look like right now." No polling
// either, unlike IngestionState - schema/storage shape doesn't change on a 10s cadence
// the way live throughput does; a manual refresh button covers the "I just generated
// traffic and want to see it reflected" case instead.

import {
	getIndexingStats,
	getClusterStatus,
	getPromotedAttributes,
	promoteAttribute,
	demoteAttribute,
	type IndexingStatsResponse,
	type ClusterStatusResponse,
	type PromotedAttributesResponse,
	type PromotedAttributeTable
} from '$lib/indexing-api';
import type { AttributeBag } from '$lib/api';

export class IndexingState {
	stats = $state.raw<IndexingStatsResponse | null>(null);
	/**
	 * Fetched alongside `stats` on every `load()`, not gated behind it - a single-node
	 * deployment gets `{ clusterModeEnabled: false, nodes: [] }` back cheaply (see
	 * ClusterQueryService's remarks), so there's no need to skip this call client-side
	 * first; IndexingClusterStatus.svelte just renders nothing when it's false.
	 */
	clusterStatus = $state.raw<ClusterStatusResponse | null>(null);
	/** Promoted attribute columns (ADR-0062, ADR-0063) - loaded with the rest, reloaded after each promote/demote. */
	promoted = $state.raw<PromotedAttributesResponse | null>(null);
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
			const [stats, clusterStatus, promoted] = await Promise.all([
				getIndexingStats(abort.signal),
				getClusterStatus(abort.signal),
				getPromotedAttributes(abort.signal)
			]);
			if (abort.signal.aborted) return;
			this.stats = stats;
			this.clusterStatus = clusterStatus;
			this.promoted = promoted;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	/** Throws the API's error message on failure - the caller shows it next to the form. */
	async promote(table: PromotedAttributeTable, bag: AttributeBag, key: string, backfill: boolean): Promise<void> {
		await promoteAttribute(table, bag, key, backfill);
		await this.load();
	}

	async demote(table: PromotedAttributeTable, columnName: string): Promise<void> {
		await demoteAttribute(table, columnName);
		await this.load();
	}

	dispose(): void {
		this.#abort?.abort();
	}
}
