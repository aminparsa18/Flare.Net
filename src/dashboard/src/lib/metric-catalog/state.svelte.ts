// Central reactive state for the Metrics catalog page (routes/metrics/catalog) - every
// metric ingested in a window with its cardinality, plus one metric's drill-down. No
// background polling, unlike the Hosts page: this is an occasional audit view, and each
// load reads every metric table, so it refreshes on demand instead.

import { getMetricCatalogDetail, listMetricCatalog, type MetricCatalogDetail, type MetricCatalogEntry } from '$lib/metric-catalog-api';
import type { MetricPointType } from '$lib/metrics-api';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '$lib/services/state.svelte';

// Same five presets as the Services tab - the server clamps to the same 5m-24h range
// (MetricCatalogQueryBuilder.MinWindowMinutes/MaxWindowMinutes). Their values are also valid
// Metrics explorer time-range presets, which "Open in explorer" relies on.
export type MetricCatalogWindowPreset = ServicesWindowPreset;
export const METRIC_CATALOG_WINDOW_PRESETS = SERVICES_WINDOW_PRESETS;

export type MetricCatalogSortColumn = 'metricName' | 'type' | 'serviceCount' | 'seriesCount' | 'sampleCount' | 'lastReceivedMs';

const SEARCH_DEBOUNCE_MS = 300;

export class MetricCatalogState {
	windowPreset = $state<MetricCatalogWindowPreset>('1h');
	search = $state('');

	metrics = $state.raw<MetricCatalogEntry[] | null>(null);
	truncated = $state(false);
	/** Epoch ms of the last successful load - what "last received N min ago" is relative to. */
	loadedAt = $state(0);
	loading = $state(false);
	error = $state<string | null>(null);

	// Highest cardinality first - the reason to open this page is finding the metric that's
	// exploding.
	sortColumn = $state<MetricCatalogSortColumn>('seriesCount');
	sortDescending = $state(true);

	/** The drill-down sheet's metric, null when closed. */
	selected = $state<{ metricName: string; type: MetricPointType } | null>(null);
	detail = $state.raw<MetricCatalogDetail | null>(null);
	detailLoading = $state(false);
	detailError = $state<string | null>(null);

	#abort: AbortController | null = null;
	#detailAbort: AbortController | null = null;
	#searchHandle: ReturnType<typeof setTimeout> | null = null;

	windowMinutes(): number {
		return METRIC_CATALOG_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		this.loading = true;
		this.error = null;
		try {
			const response = await listMetricCatalog(this.windowMinutes(), this.search, abort.signal);
			if (abort.signal.aborted) return;
			this.metrics = response.metrics;
			this.truncated = response.truncated;
			this.loadedAt = Date.now();
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	/** Explicit refresh - also re-fetches an open drill-down so both describe the same moment. */
	refresh(): void {
		void this.load();
		if (this.selected != null) void this.#loadDetail();
	}

	setWindowPreset(preset: MetricCatalogWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		this.metrics = null;
		this.refresh();
	}

	/** Debounced - fires on every keystroke from the search box. */
	setSearch(search: string): void {
		this.search = search;
		if (this.#searchHandle !== null) clearTimeout(this.#searchHandle);
		this.#searchHandle = setTimeout(() => {
			this.#searchHandle = null;
			void this.load();
		}, SEARCH_DEBOUNCE_MS);
	}

	setSort(column: MetricCatalogSortColumn): void {
		if (this.sortColumn === column) {
			this.sortDescending = !this.sortDescending;
		} else {
			this.sortColumn = column;
			// Names/types A-Z; every count and recency busiest/newest first - same per-column
			// default direction convention as HostsState.setSort.
			this.sortDescending = column !== 'metricName' && column !== 'type';
		}
	}

	sorted(): MetricCatalogEntry[] {
		const metrics = this.metrics ?? [];
		const column = this.sortColumn;
		const direction = this.sortDescending ? -1 : 1;
		return [...metrics].sort((a, b) => {
			const left = a[column];
			const right = b[column];
			const primary = typeof left === 'string' ? left.localeCompare(right as string) : (left as number) - (right as number);
			return direction * primary || a.metricName.localeCompare(b.metricName);
		});
	}

	openMetric(metricName: string, type: MetricPointType): void {
		this.selected = { metricName, type };
		this.detail = null;
		void this.#loadDetail();
	}

	closeMetric(): void {
		this.#detailAbort?.abort();
		this.selected = null;
		this.detail = null;
		this.detailError = null;
		this.detailLoading = false;
	}

	/** The open metric's catalog row, when it's in the current (possibly search-filtered) list. */
	selectedEntry(): MetricCatalogEntry | null {
		const selected = this.selected;
		if (selected == null) return null;
		return this.metrics?.find((m) => m.metricName === selected.metricName && m.type === selected.type) ?? null;
	}

	async #loadDetail(): Promise<void> {
		const selected = this.selected;
		if (selected == null) return;

		this.#detailAbort?.abort();
		const abort = new AbortController();
		this.#detailAbort = abort;

		this.detailLoading = true;
		this.detailError = null;
		try {
			const detail = await getMetricCatalogDetail(selected.metricName, selected.type, this.windowMinutes(), abort.signal);
			if (abort.signal.aborted) return;
			this.detail = detail;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.detailError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.detailLoading = false;
		}
	}

	dispose(): void {
		if (this.#searchHandle !== null) clearTimeout(this.#searchHandle);
		this.#abort?.abort();
		this.#detailAbort?.abort();
	}
}
