// Central reactive state for the /messaging page - one row per topic/queue from spans'
// `messaging.*` attributes, plus one destination's drill-down (producers, consumers,
// partitions, consumer lag). See docs-internal/adr/0056-messaging-queue-monitoring.md.
//
// No polling, same as ErrorsExplorerState: every load aggregates the window's messaging
// spans live (no pre-aggregated table), so it's an on-demand view with a manual refresh.

import {
	getMessagingDestinationDetail,
	getMessagingDestinations,
	type MessagingDestination,
	type MessagingDestinationDetailResponse
} from '$lib/messaging-api';
import { SERVICES_WINDOW_PRESETS, type ServicesWindowPreset } from '$lib/services/state.svelte';

// Same five presets as the Services tab and Hosts page - the server clamps to the same
// 5m-24h range (MessagingQueryBuilder.MinWindowMinutes/MaxWindowMinutes).
export type MessagingWindowPreset = ServicesWindowPreset;
export const MESSAGING_WINDOW_PRESETS = SERVICES_WINDOW_PRESETS;

export type MessagingSortColumn =
	| 'destination'
	| 'publishPerSecond'
	| 'consumePerSecond'
	| 'errorRate'
	| 'publishP99Ms'
	| 'consumeP99Ms'
	| 'backlog';

/** Publish + consume errors over publish + consume spans, 0-1. */
export function destinationErrorRate(d: MessagingDestination): number {
	const total = d.publishCount + d.consumeCount;
	return total === 0 ? 0 : (d.publishErrorCount + d.consumeErrorCount) / total;
}

export class MessagingState {
	windowPreset = $state<MessagingWindowPreset>('1h');
	/** '' = all services. */
	service = $state('');
	/** '' = all systems. */
	system = $state('');

	destinations = $state.raw<MessagingDestination[] | null>(null);
	/** Pickers' options - the server returns them unfiltered by the current service/system, so picking one doesn't hide the rest. */
	systems = $state.raw<string[]>([]);
	services = $state.raw<string[]>([]);
	loading = $state(false);
	error = $state<string | null>(null);

	sortColumn = $state<MessagingSortColumn>('publishPerSecond');
	sortDescending = $state(true);

	/** The drill-down sheet's destination, null when closed. */
	selected = $state.raw<MessagingDestination | null>(null);
	detail = $state.raw<MessagingDestinationDetailResponse | null>(null);
	detailLoading = $state(false);
	detailError = $state<string | null>(null);

	#abort: AbortController | null = null;
	#detailAbort: AbortController | null = null;

	#minutes(): number {
		return MESSAGING_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 60;
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		this.loading = true;
		this.error = null;
		try {
			const response = await getMessagingDestinations(this.#minutes(), { service: this.service, system: this.system }, abort.signal);
			if (abort.signal.aborted) return;
			this.destinations = response.destinations;
			this.systems = response.systems;
			this.services = response.services;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}

		if (this.selected != null) void this.#loadDetail();
	}

	/** A filter/window change is a different query - clear so the spinner shows, same as HostsState. */
	#reload(): void {
		this.destinations = null;
		void this.load();
	}

	setWindowPreset(preset: MessagingWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		this.#reload();
	}

	setService(service: string): void {
		if (this.service === service) return;
		this.service = service;
		this.#reload();
	}

	setSystem(system: string): void {
		if (this.system === system) return;
		this.system = system;
		this.#reload();
	}

	setSort(column: MessagingSortColumn): void {
		if (this.sortColumn === column) {
			this.sortDescending = !this.sortDescending;
		} else {
			this.sortColumn = column;
			// Name A-Z; every figure busiest/worst first - same per-column default direction
			// convention as HostsState.setSort.
			this.sortDescending = column !== 'destination';
		}
	}

	sorted(): MessagingDestination[] {
		const rows = this.destinations ?? [];
		const column = this.sortColumn;
		const direction = this.sortDescending ? -1 : 1;
		return [...rows].sort((a, b) => {
			switch (column) {
				case 'destination':
					return direction * (a.destination.localeCompare(b.destination) || a.system.localeCompare(b.system));
				case 'errorRate':
					return direction * (destinationErrorRate(a) - destinationErrorRate(b));
				case 'backlog': {
					// No backlog metric sorts last either way - "not collected" isn't a value between two readings.
					if (a.backlog == null && b.backlog == null) return 0;
					if (a.backlog == null) return 1;
					if (b.backlog == null) return -1;
					return direction * (a.backlog - b.backlog);
				}
				default:
					return direction * (a[column] - b[column]);
			}
		});
	}

	open(destination: MessagingDestination): void {
		this.selected = destination;
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
			const detail = await getMessagingDestinationDetail(selected.system, selected.destination, this.#minutes(), this.service, abort.signal);
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
