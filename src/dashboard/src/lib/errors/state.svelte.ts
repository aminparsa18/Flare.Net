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
import type { ErrorsDeepLinkState } from '$lib/deep-links';

export interface ErrorsFilterState {
	timeRangePreset: TimeRangePreset;
	/** Set only while timeRangePreset === 'custom' - same shape as MetricsFilterState.customRange. The toolbar never offers 'custom'; the only producer is a fired exception alert's `?state=` link (applyDeepLinkState). */
	customRange: { from: Date; to: Date } | null;
	services: string[];
	/**
	 * Narrows the groups table to one exception type (and, when non-empty, one exact message) -
	 * what an ExceptionCount alert rule counts. Client-side over the fetched groups, like the
	 * table's sort: the API's ExceptionFilter has no type field, and the rule's own services +
	 * window already bound the fetch. '' = no narrowing. Only set by applyDeepLinkState.
	 */
	exceptionType: string;
	exceptionMessage: string;
}

/** Mirrors `ExceptionGroupQueryBuilder.MaxTopN` on the API side. */
const MAX_GROUPS = 1_000;

export type ErrorsSortColumn = 'exceptionType' | 'exceptionMessage' | 'occurrenceCount' | 'affectedServices' | 'firstSeen' | 'lastSeen';

export class ErrorsExplorerState {
	filter = $state<ErrorsFilterState>({ timeRangePreset: '1h', customRange: null, services: [], exceptionType: '', exceptionMessage: '' });

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
		return resolveTimeRange(this.filter.timeRangePreset, this.filter.customRange ?? undefined);
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
			// The type narrowing is client-side (see ErrorsFilterState.exceptionType), so fetch
			// the API's max rather than the default top 200 - the narrowed type mustn't fall
			// off the end of a busy window's ranking.
			const topN = this.filter.exceptionType ? MAX_GROUPS : undefined;
			const res = await getExceptionGroups({ filter: this.buildFilter(this.#resolvedRange()), topN }, abort.signal);
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
		if (preset !== 'custom') this.filter.customRange = null;
		void this.runSearch();
	}

	/**
	 * Restores a fired exception alert's scope (`?state=`, see `parseErrorsStateDeepLinkParam`)
	 * - the rule's services, type/message and evaluated window - then opens the occurrences
	 * of the one group it matches, when exactly one does (always, for a rule that narrows to
	 * one message; the table otherwise lists every message of that type).
	 */
	async applyDeepLinkState(state: ErrorsDeepLinkState): Promise<void> {
		this.filter = {
			timeRangePreset: 'custom',
			customRange: state.customRange,
			services: state.services,
			exceptionType: state.exceptionType,
			exceptionMessage: state.exceptionMessage
		};
		await this.runSearch();
		const matches = this.visibleGroups();
		if (matches.length === 1) this.selectGroup(matches[0]);
	}

	/** Clears just the type/message narrowing (the toolbar chip's remove button). */
	clearExceptionType(): void {
		this.filter.exceptionType = '';
		this.filter.exceptionMessage = '';
	}

	/** `groups` minus anything outside the type/message narrowing - what the table shows, before sorting. */
	visibleGroups(): ExceptionGroup[] {
		const { exceptionType, exceptionMessage } = this.filter;
		if (!exceptionType) return this.groups;
		return this.groups.filter(
			(g) => g.exceptionType === exceptionType && (!exceptionMessage || g.exceptionMessage === exceptionMessage)
		);
	}

	setServices(services: string[]): void {
		this.filter.services = services;
		void this.runSearch();
	}

	/** Whether the toolbar's "Clear filters" button has anything to do. */
	hasActiveFilters(): boolean {
		return this.filter.services.length > 0 || this.filter.exceptionType !== '';
	}

	/** Toolbar's "Clear filters" button - same "leave the time range alone" scope LogsExplorerState.resetFilters documents for itself. */
	resetFilters(): void {
		this.filter.services = [];
		this.clearExceptionType();
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
		return [...this.visibleGroups()].sort((a, b) => {
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
