// Central reactive state for the Traces page's "Services" tab - the "which service is
// unhealthy right now" per-service RED-metrics table (see
// docs-internal/planning/roadmap.md's now-removed "Per-service RED-metrics overview"
// item; folded into a tab on /traces rather than given its own route - see that page's
// own remarks). Polls on the same 10s cadence as IngestionState, same rationale: this is
// a "what's happening right now" view, not a point-in-time snapshot - a stale error rate
// is the one thing this tab must never show silently.

import {
	getServiceOverview,
	getServiceDependencyGraph,
	getApdexThresholds,
	setApdexThreshold as setApdexThresholdRequest,
	resetApdexThreshold as resetApdexThresholdRequest,
	type ServiceMetrics,
	type ServiceDependencyGraph,
	type ResourceAttributeFilter,
	type ApdexThresholds
} from '$lib/services-api';
import * as m from '$lib/paraglide/messages';

export type ServicesWindowPreset = '5m' | '15m' | '1h' | '6h' | '24h';

// No `label` field - same reasoning as ingestion/state.svelte.ts's INGESTION_WINDOW_PRESETS:
// a module-scope const can't reflect a per-request/live-switched locale. Use
// servicesWindowPresetLabel() below instead, called fresh at each use.
export const SERVICES_WINDOW_PRESETS: { value: ServicesWindowPreset; minutes: number }[] = [
	{ value: '5m', minutes: 5 },
	{ value: '15m', minutes: 15 },
	{ value: '1h', minutes: 60 },
	{ value: '6h', minutes: 360 },
	{ value: '24h', minutes: 1440 }
];

export function servicesWindowPresetLabel(preset: ServicesWindowPreset): string {
	switch (preset) {
		case '5m':
			return m.servicesWindowPreset_last5m();
		case '15m':
			return m.servicesWindowPreset_last15m();
		case '1h':
			return m.servicesWindowPreset_last1h();
		case '6h':
			return m.servicesWindowPreset_last6h();
		case '24h':
			return m.servicesWindowPreset_last24h();
	}
}

export type ServicesSortColumn = 'serviceName' | 'requestsPerSecond' | 'errorRate' | 'p50DurationMs' | 'p95DurationMs' | 'p99DurationMs' | 'apdexScore';

const POLL_INTERVAL_MS = 10_000;

export class ServicesState {
	windowPreset = $state<ServicesWindowPreset>('15m');
	services = $state.raw<ServiceMetrics[] | null>(null);
	loading = $state(false);
	error = $state<string | null>(null);

	// The Services tab's filter chips (docs-internal/planning/roadmap.md's now-removed
	// "Resource-attribute filtering on the Traces > Services tab" item) - one set narrows
	// the Table view, Map view, and (via ServiceCallBreakdownDialog reading this same
	// field) the per-node drill-down together, same "one filter shape reused across
	// sibling views" precedent LogFilter already sets for the Logs page.
	resourceAttributes = $state<ResourceAttributeFilter[]>([]);

	// The dependency map, rendered below the table (not behind a separate tab) - both
	// views share this one window, so both are fetched together on every load()/poll.
	graph = $state.raw<ServiceDependencyGraph | null>(null);

	// The configured Apdex threshold default + per-service overrides
	// (docs-internal/adr/0032-apdex-score-per-service.md) - fetched once (not on every
	// 10s poll like services/graph above, since it's rarely-changed admin config, not
	// "what's happening right now" telemetry) and re-fetched after a save/reset via
	// ApdexThresholdPopover. Each service's own effective score/threshold still comes
	// from `services` (ServiceMetrics.apdexScore/apdexThresholdMs) on every poll; this is
	// only needed for the popover's "what's the default" / "does this service have an
	// override" UI.
	apdexThresholds = $state.raw<ApdexThresholds | null>(null);

	// The map's per-node drill-down selection - same plain-field-mutated-by-the-clicking-
	// component, cleared-on-close precedent as TraceDetailState.selectedSpanId (see
	// SpanDetailSheet.svelte's own onOpenChange). Lives here rather than as local state on
	// ServiceDependencyGraph.svelte because the dialog reading it (ServiceCallBreakdownDialog)
	// is rendered as this tab's own sibling, not nested inside the graph component.
	selectedService = $state<string | null>(null);

	// Client-side only - the server always returns RequestCount DESC (see
	// ServiceOverviewQueryBuilder's ORDER BY); re-sorting a small, already-fetched list
	// client-side is simpler than threading a sort param through the query, and matches
	// the "server picks a sane default, the table re-sorts freely" precedent
	// IndexingTablesTable already sets for this codebase.
	sortColumn = $state<ServicesSortColumn>('requestsPerSecond');
	sortDescending = $state(true);

	#abort: AbortController | null = null;
	#pollHandle: ReturnType<typeof setInterval> | null = null;

	#minutes(): number {
		return SERVICES_WINDOW_PRESETS.find((p) => p.value === this.windowPreset)?.minutes ?? 15;
	}

	async load(): Promise<void> {
		this.#abort?.abort();
		const abort = new AbortController();
		this.#abort = abort;

		// Only show the spinner on the very first load for this window - a background poll
		// refresh shouldn't blank the table+map every 10s while data is already on screen
		// (same reasoning as IngestionState.load). Table and map share one window, so both
		// are fetched together, in parallel - not "load whichever is visible": both are
		// always visible now (see the Traces page's own remarks on why the tab no longer
		// switches between them).
		const hasData = this.services != null && this.graph != null;
		if (!hasData) this.loading = true;
		this.error = null;
		try {
			const minutes = this.#minutes();
			const [overview, graph] = await Promise.all([
				getServiceOverview(minutes, this.resourceAttributes, abort.signal),
				getServiceDependencyGraph(minutes, this.resourceAttributes, abort.signal),
				this.apdexThresholds == null ? this.#loadApdexThresholds() : Promise.resolve(),
			]);
			if (abort.signal.aborted) return;
			this.services = overview.services;
			this.graph = graph;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	setWindowPreset(preset: ServicesWindowPreset): void {
		if (this.windowPreset === preset) return;
		this.windowPreset = preset;
		// A new window is a genuinely different pair of queries, not a background refresh -
		// force the spinner.
		this.services = null;
		this.graph = null;
		void this.load();
	}

	/** Replaces the filter-chip set and re-fetches - same "a changed filter is a genuinely different pair of queries, force the spinner" call as setWindowPreset. */
	setResourceAttributes(resourceAttributes: ResourceAttributeFilter[]): void {
		this.resourceAttributes = resourceAttributes;
		this.services = null;
		this.graph = null;
		void this.load();
	}

	async #loadApdexThresholds(): Promise<void> {
		try {
			this.apdexThresholds = await getApdexThresholds();
		} catch {
			// Non-fatal - the Apdex column still renders (each row already carries its own
			// effective apdexScore/apdexThresholdMs from getServiceOverview); only the
			// per-row edit popover's "what's the default"/"has an override" affordance is
			// degraded until the next successful load().
		}
	}

	/** Saves a per-service Apdex threshold override (Admin-only server-side), then
	 * refetches both the threshold list and the overview so the table reflects the new
	 * score immediately rather than waiting for the next poll. */
	async setApdexThreshold(serviceName: string, thresholdMs: number): Promise<void> {
		await setApdexThresholdRequest(serviceName, thresholdMs);
		await this.#loadApdexThresholds();
		await this.load();
	}

	/** Reverts a service to the default Apdex threshold - same refetch-immediately shape as {@link setApdexThreshold}. */
	async resetApdexThreshold(serviceName: string): Promise<void> {
		await resetApdexThresholdRequest(serviceName);
		await this.#loadApdexThresholds();
		await this.load();
	}

	setSort(column: ServicesSortColumn): void {
		if (this.sortColumn === column) {
			this.sortDescending = !this.sortDescending;
		} else {
			this.sortColumn = column;
			// Service name reads naturally ascending (A-Z); every metric column reads
			// naturally descending (worst/busiest first) - same "pick the useful default
			// direction per column" convention as most sortable-table implementations.
			this.sortDescending = column !== 'serviceName';
		}
	}

	sorted(): ServiceMetrics[] {
		const services = this.services ?? [];
		const column = this.sortColumn;
		const direction = this.sortDescending ? -1 : 1;
		return [...services].sort((a, b) => {
			const left = a[column];
			const right = b[column];
			if (typeof left === 'string' || typeof right === 'string') {
				return direction * String(left).localeCompare(String(right));
			}
			// apdexScore is nullable (a service with 0 requests) - sorts as lowest
			// regardless of direction, same "missing data reads as worst" convention
			// errorRateClass's own escalation implies for this table.
			const leftNum = (left as number | null) ?? -1;
			const rightNum = (right as number | null) ?? -1;
			return direction * (leftNum - rightNum);
		});
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
		this.#abort?.abort();
	}
}
