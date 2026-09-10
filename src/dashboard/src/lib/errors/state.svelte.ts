// Central reactive state for the /errors page - grouped exceptions (type/message rollup)
// plus the selected group's click-through occurrence drill-down. Mirrors
// TracesExplorerState's shape (`$lib/traces/state.svelte.ts`): a class with `$state`
// fields, provided via `errorsExplorerContext` rather than passed as props. No
// polling/auto-refresh here (unlike ServicesState) - this is an investigate/browse view
// over a chosen window, not a live snapshot; a manual refresh re-runs runSearch().

import {
	getExceptionGroups,
	getExceptionOccurrences,
	type ExceptionFilter,
	type ExceptionGroup,
	type ExceptionOccurrencesResponse
} from '$lib/errors-api';
import { resolveTimeRange, type TimeRangePreset, type ResolvedTimeRange } from '$lib/logs/time-range';

export interface ErrorsFilterState {
	timeRangePreset: TimeRangePreset;
	services: string[];
}

export type ErrorsSortColumn = 'exceptionType' | 'exceptionMessage' | 'occurrenceCount' | 'affectedServices' | 'firstSeen' | 'lastSeen';

export class ErrorsExplorerState {
	filter = $state<ErrorsFilterState>({ timeRangePreset: '1h', services: [] });

	// Never mutated in place, always a wholesale reassignment on each search - same
	// $state.raw rationale TracesExplorerState.traces documents for its own field.
	groups = $state.raw<ExceptionGroup[]>([]);

	loading = $state(false);
	error = $state<string | null>(null);

	// No dedicated "list distinct services" endpoint here either (same gap
	// TracesExplorerState.knownServices documents for spans) - accumulated across every
	// search's results instead of a separate broad query, since the default (no service
	// filter) search already spans every service. Deliberately additive, never replaced
	// wholesale, so narrowing the service filter doesn't also shrink the picklist itself.
	knownServices = $state.raw<string[]>([]);

	// The groups-table row currently drilled into (opens ExceptionOccurrenceDialog) - same
	// "selection lives on the shared state, not as local component state" precedent
	// ServicesState.selectedService sets, for the same reason (the dialog is this page's
	// own sibling, not nested inside the table).
	selectedGroup = $state<ExceptionGroup | null>(null);
	occurrences = $state.raw<ExceptionOccurrencesResponse | null>(null);
	occurrencesLoading = $state(false);
	occurrencesError = $state<string | null>(null);

	// Client-side only - the server always returns OccurrenceCount DESC (see
	// ExceptionGroupQueryBuilder's ORDER BY); re-sorting a small, already-fetched list
	// client-side is simpler than threading a sort param through the API, same precedent
	// ServicesState.sortColumn/sorted() already sets for this codebase.
	sortColumn = $state<ErrorsSortColumn>('occurrenceCount');
	sortDescending = $state(true);

	#searchAbort: AbortController | null = null;
	#occurrencesAbort: AbortController | null = null;

	#resolvedRange(): ResolvedTimeRange | null {
		return resolveTimeRange(this.filter.timeRangePreset);
	}

	buildFilter(range: ResolvedTimeRange | null): ExceptionFilter {
		const filter: ExceptionFilter = {};
		if (range) {
			filter.from = range.from;
			filter.to = range.to;
		}
		if (this.filter.services.length) filter.services = [...this.filter.services];
		return filter;
	}

	async runSearch(): Promise<void> {
		this.#searchAbort?.abort();
		const abort = new AbortController();
		this.#searchAbort = abort;

		this.loading = true;
		this.error = null;
		try {
			const res = await getExceptionGroups({ filter: this.buildFilter(this.#resolvedRange()) }, abort.signal);
			if (abort.signal.aborted) return;
			this.groups = res.groups;
			this.knownServices = [...new Set([...this.knownServices, ...res.groups.flatMap((g) => g.affectedServices)])].sort();
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	setTimeRangePreset(preset: TimeRangePreset): void {
		this.filter.timeRangePreset = preset;
		void this.runSearch();
	}

	setServices(services: string[]): void {
		this.filter.services = services;
		void this.runSearch();
	}

	setSort(column: ErrorsSortColumn): void {
		if (this.sortColumn === column) {
			this.sortDescending = !this.sortDescending;
		} else {
			this.sortColumn = column;
			// Type/message read naturally ascending (A-Z); every count/recency column reads
			// naturally descending (most-frequent/most-recent first) - same "pick the useful
			// default direction per column" convention as ServicesState.setSort.
			this.sortDescending = column !== 'exceptionType' && column !== 'exceptionMessage';
		}
	}

	sorted(): ExceptionGroup[] {
		const column = this.sortColumn;
		const direction = this.sortDescending ? -1 : 1;
		return [...this.groups].sort((a, b) => {
			switch (column) {
				case 'exceptionType':
					return direction * a.exceptionType.localeCompare(b.exceptionType);
				case 'exceptionMessage':
					return direction * a.exceptionMessage.localeCompare(b.exceptionMessage);
				case 'occurrenceCount':
					return direction * (a.occurrenceCount - b.occurrenceCount);
				case 'affectedServices':
					return direction * (a.affectedServices.length - b.affectedServices.length);
				case 'firstSeen':
					return direction * a.firstSeen.localeCompare(b.firstSeen);
				case 'lastSeen':
					return direction * a.lastSeen.localeCompare(b.lastSeen);
			}
		});
	}

	/** Opens (or, passed `null`, closes) the occurrences drill-down for one group - same "one setter drives fetch + dialog visibility" shape as ServicesState.selectedService's own consumer (ServiceCallBreakdownDialog). */
	selectGroup(group: ExceptionGroup | null): void {
		this.#occurrencesAbort?.abort();
		this.selectedGroup = group;
		this.occurrences = null;
		this.occurrencesError = null;

		if (!group) {
			this.occurrencesLoading = false;
			return;
		}

		const abort = new AbortController();
		this.#occurrencesAbort = abort;
		this.occurrencesLoading = true;

		getExceptionOccurrences(
			{
				filter: this.buildFilter(this.#resolvedRange()),
				exceptionType: group.exceptionType,
				exceptionMessage: group.exceptionMessage
			},
			abort.signal
		)
			.then((res) => {
				if (abort.signal.aborted) return;
				this.occurrences = res;
			})
			.catch((err) => {
				if (abort.signal.aborted) return;
				this.occurrencesError = err instanceof Error ? err.message : String(err);
			})
			.finally(() => {
				if (!abort.signal.aborted) this.occurrencesLoading = false;
			});
	}

	dispose(): void {
		this.#searchAbort?.abort();
		this.#occurrencesAbort?.abort();
	}
}
