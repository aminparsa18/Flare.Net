// Central reactive state for the /external-apis page - one row per external domain our
// services call, from client spans' `server.address`/`url.full` attributes, plus one
// domain's drill-down (endpoints, status codes, callers, top errors). See
// docs-internal/adr/0071-external-api-monitoring.md.
//
// No polling, same as MessagingState: every load aggregates the window's client spans live
// (no pre-aggregated table), so it's an on-demand view with a manual refresh.

import {
	getExternalDomainDetail,
	getExternalDomains,
	type ExternalDomain,
	type ExternalDomainDetailResponse,
	type ExternalEndpoint
} from '$lib/external-apis-api';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '$lib/services/state.svelte';

// Same five presets as the Services tab and Messaging page - the server clamps to the same
// 5m-24h range (ExternalApiQueryBuilder.MinWindowMinutes/MaxWindowMinutes).
export type ExternalApisWindowPreset = ServicesWindowPreset;
export const EXTERNAL_APIS_WINDOW_PRESETS = SERVICES_WINDOW_PRESETS;

export type ExternalDomainSortColumn = 'domain' | 'perSecond' | 'errorRate' | 'p95Ms' | 'p99Ms' | 'endpointCount' | 'lastSeenUnixMs';

export type ExternalEndpointSortColumn = 'endpoint' | 'perSecond' | 'errorRate' | 'p50Ms' | 'p95Ms' | 'p99Ms' | 'lastSeenUnixMs';

/** Errors over calls, 0-1. */
export function errorRate(row: { callCount: number; errorCount: number }): number {
	return row.callCount === 0 ? 0 : row.errorCount / row.callCount;
}

/** Names A-Z; every figure busiest/worst/latest first - same per-column default as MessagingState.setSort. */
function defaultDescending(column: string): boolean {
	return column !== 'domain' && column !== 'endpoint';
}

export class ExternalApisState {
	windowPreset = $state<ExternalApisWindowPreset>('1h');
	/** '' = all calling services. */
	service = $state('');

	domains = $state.raw<ExternalDomain[] | null>(null);
	/** The caller picker's options - returned unfiltered by the current service, so picking one doesn't hide the rest. */
	services = $state.raw<string[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	sortColumn = $state<ExternalDomainSortColumn>('perSecond');
	sortDescending = $state(true);

	/** The drill-down sheet's domain, null when closed. */
	selected = $state.raw<ExternalDomain | null>(null);
	detail = $state.raw<ExternalDomainDetailResponse | null>(null);
	detailLoading = $state(false);
	detailError = $state<string | null>(null);

	endpointSortColumn = $state<ExternalEndpointSortColumn>('perSecond');
	endpointSortDescending = $state(true);

	#abort: AbortController | null = null;
	#detailAbort: AbortController | null = null;

	#minutes(): number {
		return EXTERNAL_APIS_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		this.loading = true;
		this.error = null;
		try {
			const response = await getExternalDomains(this.#minutes(), this.service, abort.signal);
			if (abort.signal.aborted) return;
			this.domains = response.domains;
			this.services = response.services;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}

		if (this.selected != null) void this.#loadDetail();
	}

	/** A filter/window change is a different query - clear so the spinner shows, same as MessagingState. */
	#reload(): void {
		this.domains = null;
		void this.load();
	}

	setWindowPreset(preset: ExternalApisWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		this.#reload();
	}

	setService(service: string): void {
		if (this.service === service) return;
		this.service = service;
		this.#reload();
	}

	setSort(column: ExternalDomainSortColumn): void {
		if (this.sortColumn === column) {
			this.sortDescending = !this.sortDescending;
		} else {
			this.sortColumn = column;
			this.sortDescending = defaultDescending(column);
		}
	}

	setEndpointSort(column: ExternalEndpointSortColumn): void {
		if (this.endpointSortColumn === column) {
			this.endpointSortDescending = !this.endpointSortDescending;
		} else {
			this.endpointSortColumn = column;
			this.endpointSortDescending = defaultDescending(column);
		}
	}

	sorted(): ExternalDomain[] {
		const column = this.sortColumn;
		const direction = this.sortDescending ? -1 : 1;
		return [...(this.domains ?? [])].sort((a, b) => {
			switch (column) {
				case 'domain':
					return direction * a.domain.localeCompare(b.domain);
				case 'errorRate':
					return direction * (errorRate(a) - errorRate(b));
				default:
					return direction * (a[column] - b[column]);
			}
		});
	}

	sortedEndpoints(): ExternalEndpoint[] {
		const column = this.endpointSortColumn;
		const direction = this.endpointSortDescending ? -1 : 1;
		return [...(this.detail?.endpoints ?? [])].sort((a, b) => {
			switch (column) {
				case 'endpoint':
					return direction * (a.endpoint.localeCompare(b.endpoint) || a.method.localeCompare(b.method));
				case 'errorRate':
					return direction * (errorRate(a) - errorRate(b));
				default:
					return direction * (a[column] - b[column]);
			}
		});
	}

	/** A deep link's `?window=`, ignored when it isn't a known preset. */
	applyWindowParam(value: string | null): void {
		const preset = EXTERNAL_APIS_WINDOW_PRESETS.find((p) => p.value === value);
		if (preset) this.windowPreset = preset.value;
	}

	/** Opens a deep link's `?domain=` once the table has loaded; a domain with no calls in the window just leaves the table showing. */
	openByName(name: string): void {
		const domain = this.domains?.find((d) => d.domain === name);
		if (domain) this.open(domain);
	}

	open(domain: ExternalDomain): void {
		this.selected = domain;
		this.detail = null;
		void this.#loadDetail();
	}

	close(): void {
		this.#detailAbort?.abort();
		this.selected = null;
		this.detail = null;
		this.detailError = null;
		this.detailLoading = false;
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
			const detail = await getExternalDomainDetail(selected.domain, this.#minutes(), this.service, abort.signal);
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
		this.#abort?.abort();
		this.#detailAbort?.abort();
	}
}
