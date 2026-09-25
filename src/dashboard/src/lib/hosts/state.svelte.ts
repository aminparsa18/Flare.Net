// Central reactive state for the Hosts page (routes/hosts) - the inventory of hosts that
// ship OTel `hostmetrics`-receiver metrics, plus one host's drill-down. Distinct from the
// Resources page's HostStatsState, which reports on the machine Flare.Api itself runs on
// via its own poller, not on hosts sending telemetry.
//
// Polls on a 30s cadence - slower than the Services tab's 10s, because the hostmetrics
// receiver's own default collection interval is 60s; polling faster would mostly re-fetch
// identical numbers.

import { getHostMetrics, listHosts, type HostMetricsResponse, type HostSummary } from '$lib/hosts-api';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '$lib/services/state.svelte';

// Same five presets as the Services tab - the server clamps both to the same 5m-24h range
// (HostInventoryQueryBuilder.MinWindowMinutes/MaxWindowMinutes), so reusing its presets
// and labels avoids a second, identical set.
export type HostsWindowPreset = ServicesWindowPreset;
export const HOSTS_WINDOW_PRESETS = SERVICES_WINDOW_PRESETS;

export type HostsSortColumn = 'hostName' | 'cpuPercent' | 'memoryPercent' | 'diskPercent' | 'loadAverage15m' | 'lastSeen';

const POLL_INTERVAL_MS = 30_000;
const SEARCH_DEBOUNCE_MS = 300;

export class HostsState {
	windowPreset = $state<HostsWindowPreset>('1h');
	search = $state('');
	/** Empty string = all OS types. */
	osType = $state('');

	hosts = $state.raw<HostSummary[] | null>(null);
	truncated = $state(false);
	/** Epoch ms of the last successful load - the "now" that "last seen N min ago" labels are relative to, so they refresh with each poll. */
	loadedAt = $state(0);
	loading = $state(false);
	error = $state<string | null>(null);

	/**
	 * Every `os.type` seen so far, across loads - not just the current result's. With an OS
	 * filter applied the result only contains that one OS, so deriving the picker's options
	 * from it would make the other choices disappear the moment one is picked.
	 */
	knownOsTypes = $state.raw<string[]>([]);

	sortColumn = $state<HostsSortColumn>('hostName');
	sortDescending = $state(false);

	/** The drill-down sheet's host, null when closed. */
	selectedHost = $state<string | null>(null);
	detail = $state.raw<HostMetricsResponse | null>(null);
	detailLoading = $state(false);
	detailError = $state<string | null>(null);

	#abort: AbortController | null = null;
	#detailAbort: AbortController | null = null;
	#pollHandle: ReturnType<typeof setInterval> | null = null;
	#searchHandle: ReturnType<typeof setTimeout> | null = null;

	#minutes(): number {
		return HOSTS_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		// Spinner only on a first/filter-changed load, not on a background poll - same
		// reasoning as ServicesState.load.
		if (this.hosts == null) this.loading = true;
		this.error = null;
		try {
			const response = await listHosts(this.#minutes(), { search: this.search, osType: this.osType }, abort.signal);
			if (abort.signal.aborted) return;
			this.hosts = response.hosts;
			this.truncated = response.truncated;
			this.loadedAt = Date.now();
			this.#rememberOsTypes(response.hosts);
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}

		if (this.selectedHost != null) void this.#loadDetail(false);
	}

	#rememberOsTypes(hosts: HostSummary[]): void {
		const known = new Set(this.knownOsTypes);
		const before = known.size;
		for (const host of hosts) {
			if (host.osType) known.add(host.osType);
		}
		if (known.size !== before) this.knownOsTypes = [...known].sort();
	}

	/** A filter/window change is a genuinely different query - clear and force the spinner, same as ServicesState.setWindowPreset. */
	#reload(): void {
		this.hosts = null;
		void this.load();
	}

	setWindowPreset(preset: HostsWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		this.detail = null;
		this.#reload();
	}

	setOsType(osType: string): void {
		if (this.osType === osType) return;
		this.osType = osType;
		this.#reload();
	}

	/** Debounced - fires on every keystroke from the search box. */
	setSearch(search: string): void {
		this.search = search;
		if (this.#searchHandle !== null) clearTimeout(this.#searchHandle);
		this.#searchHandle = setTimeout(() => {
			this.#searchHandle = null;
			this.#reload();
		}, SEARCH_DEBOUNCE_MS);
	}

	setSort(column: HostsSortColumn): void {
		if (this.sortColumn === column) {
			this.sortDescending = !this.sortDescending;
		} else {
			this.sortColumn = column;
			// Host name A-Z; every metric column busiest/most-recent first - same per-column
			// default direction convention as ServicesState.setSort.
			this.sortDescending = column !== 'hostName';
		}
	}

	sorted(): HostSummary[] {
		const hosts = this.hosts ?? [];
		const column = this.sortColumn;
		const direction = this.sortDescending ? -1 : 1;
		return [...hosts].sort((a, b) => {
			if (column === 'hostName' || column === 'lastSeen') {
				// ISO-8601 UTC timestamps sort correctly as strings.
				return direction * a[column].localeCompare(b[column]);
			}
			// A missing metric sorts last in either direction - "no data" isn't a value
			// that belongs between two real readings.
			const left = a[column];
			const right = b[column];
			if (left == null && right == null) return 0;
			if (left == null) return 1;
			if (right == null) return -1;
			return direction * (left - right);
		});
	}

	openHost(hostName: string): void {
		this.selectedHost = hostName;
		this.detail = null;
		void this.#loadDetail(true);
	}

	closeHost(): void {
		this.#detailAbort?.abort();
		this.selectedHost = null;
		this.detail = null;
		this.detailError = null;
		this.detailLoading = false;
	}

	async #loadDetail(showSpinner: boolean): Promise<void> {
		const hostName = this.selectedHost;
		if (hostName == null) return;

		this.#detailAbort?.abort();
		const abort = new AbortController();
		this.#detailAbort = abort;

		if (showSpinner) this.detailLoading = true;
		this.detailError = null;
		try {
			const detail = await getHostMetrics(hostName, this.#minutes(), abort.signal);
			if (abort.signal.aborted) return;
			this.detail = detail;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.detailError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.detailLoading = false;
		}
	}

	startPolling(): void {
		this.stopPolling();
		this.#pollHandle = setInterval(() => void this.load(), POLL_INTERVAL_MS);
	}

	stopPolling(): void {
		if (this.#pollHandle !== null) {
			clearInterval(this.#pollHandle);
			this.#pollHandle = null;
		}
	}

	dispose(): void {
		this.stopPolling();
		if (this.#searchHandle !== null) clearTimeout(this.#searchHandle);
		this.#abort?.abort();
		this.#detailAbort?.abort();
	}
}
