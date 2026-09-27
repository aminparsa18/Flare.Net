// Central reactive state for the Metrics catalog page (routes/metrics/catalog) - every
// metric ingested in a window with its cardinality, plus one metric's drill-down. No
// background polling, unlike the Hosts page: this is an occasional audit view, and each
// load reads every metric table, so it refreshes on demand instead.

import {
	getMetricCatalogDetail,
	inspectMetric,
	listMetricCatalog,
	resetMetricMetadataOverride,
	setMetricMetadataOverride,
	type MetricCatalogDetail,
	type MetricCatalogEntry,
	type MetricInspectResult
} from '$lib/metric-catalog-api';
import type { MetricPointType } from '$lib/metrics-api';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '$lib/services/state.svelte';

// Same five presets as the Services tab - the server clamps to the same 5m-24h range
// (MetricCatalogQueryBuilder.MinWindowMinutes/MaxWindowMinutes). Their values are also valid
// Metrics explorer time-range presets, which "Open in explorer" relies on.
export type MetricCatalogWindowPreset = ServicesWindowPreset;
export const METRIC_CATALOG_WINDOW_PRESETS = SERVICES_WINDOW_PRESETS;

export type MetricCatalogSortColumn = 'metricName' | 'type' | 'serviceCount' | 'seriesCount' | 'sampleCount' | 'lastReceivedMs';

const SEARCH_DEBOUNCE_MS = 300;

export type MetricCatalogDetailView = 'overview' | 'inspect';

// The inspect view is per-sample, so its window is short and separate from the catalog's -
// the server clamps to 5m-1h (MetricInspectQueryBuilder).
export const METRIC_INSPECT_WINDOWS = [5, 15, 30, 60] as const;
export const METRIC_INSPECT_BUCKETS = [10, 30, 60, 300] as const;

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

	detailView = $state<MetricCatalogDetailView>('overview');

	inspectWindowMinutes = $state<number>(15);
	inspectBucketSeconds = $state<number>(60);
	/** Null = every service. Defaults to the metric's busiest service on first open. */
	inspectService = $state<string | null>(null);
	inspect = $state.raw<MetricInspectResult | null>(null);
	inspectLoading = $state(false);
	inspectError = $state<string | null>(null);

	#abort: AbortController | null = null;
	#detailAbort: AbortController | null = null;
	#inspectAbort: AbortController | null = null;
	#inspectServiceChosen = false;
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
		if (this.selected != null && this.detailView === 'inspect') void this.#loadInspect();
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
		this.#inspectAbort?.abort();
		this.selected = { metricName, type };
		this.detail = null;
		this.detailView = 'overview';
		this.inspect = null;
		this.inspectError = null;
		this.inspectLoading = false;
		this.inspectService = null;
		this.#inspectServiceChosen = false;
		void this.#loadDetail();
	}

	closeMetric(): void {
		this.#detailAbort?.abort();
		this.#inspectAbort?.abort();
		this.selected = null;
		this.detail = null;
		this.detailError = null;
		this.detailLoading = false;
		this.inspect = null;
	}

	setDetailView(view: MetricCatalogDetailView): void {
		this.detailView = view;
		if (view !== 'inspect') return;
		// Default to the busiest service: merging series is only what a chart does within one
		// service, so that's the representative starting point.
		if (!this.#inspectServiceChosen) {
			this.inspectService = this.detail?.services[0]?.serviceName ?? null;
			this.#inspectServiceChosen = true;
		}
		if (this.inspect == null) void this.#loadInspect();
	}

	setInspectOptions(options: { windowMinutes?: number; bucketSeconds?: number; service?: string | null }): void {
		if (options.windowMinutes !== undefined) this.inspectWindowMinutes = options.windowMinutes;
		if (options.bucketSeconds !== undefined) this.inspectBucketSeconds = options.bucketSeconds;
		if (options.service !== undefined) this.inspectService = options.service;
		void this.#loadInspect();
	}

	/** Admin-only on the server. Reloads the list and drill-down so both show the new values. */
	async saveMetadataOverride(unit: string | null, description: string | null, treatAsCounter: boolean): Promise<void> {
		const selected = this.selected;
		if (selected == null) return;
		await setMetricMetadataOverride(selected.metricName, unit, description, treatAsCounter);
		this.refresh();
	}

	async resetMetadataOverride(): Promise<void> {
		const selected = this.selected;
		if (selected == null) return;
		await resetMetricMetadataOverride(selected.metricName);
		this.refresh();
	}

	async #loadInspect(): Promise<void> {
		const selected = this.selected;
		if (selected == null) return;

		this.#inspectAbort?.abort();
		const abort = new AbortController();
		this.#inspectAbort = abort;

		this.inspectLoading = true;
		this.inspectError = null;
		try {
			const result = await inspectMetric(
				selected.metricName,
				selected.type,
				{ windowMinutes: this.inspectWindowMinutes, bucketWidthSeconds: this.inspectBucketSeconds, serviceName: this.inspectService },
				abort.signal
			);
			if (abort.signal.aborted) return;
			this.inspect = result;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.inspectError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.inspectLoading = false;
		}
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
		this.#inspectAbort?.abort();
	}
}
