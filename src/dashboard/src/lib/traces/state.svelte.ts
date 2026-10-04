// Central reactive state for the Traces list page - mirrors LogsExplorerState's shape
// (`$lib/logs/state.svelte.ts`): a class with `$state` fields, provided via
// `tracesExplorerContext` rather than passed as props. No live-tail here (see
// Planning.md's traces roadmap slice - explicitly out of scope for this pass; the
// waterfall/detail view is the value being built, not a live span firehose), so this is
// simpler than LogsExplorerState: no connection/live/dropped-count fields at all.

import { regional } from '$lib/regional/prefs.svelte';
import { SPAN_SORT_KEYS, getSpanAttributeValues, searchSpans, type SpanAttributeFilter, type SpanDto, type SpanFilter, type SpanSortKey, type TraceStructureFilter } from '$lib/traces-api';
import { resolveTimeRange, type TimeRangePreset, type ResolvedTimeRange } from '$lib/logs/time-range';
import { findCaseSuggestions, applyCaseSuggestion as applyCaseSuggestionTo, type CaseSuggestion } from '$lib/case-suggestions';
import { durationBucketRange } from './duration-buckets';

const PAGE_SIZE = 100;

/** How often `autoRefreshEnabled` re-runs the current search - see its own remarks. Same order of magnitude as ServicesState's own POLL_INTERVAL_MS (10s), picked longer since a full trace-list re-search is a heavier query than the Services RED rollup. */
const AUTO_REFRESH_INTERVAL_MS = 30_000;

export interface TracesFilterState {
	timeRangePreset: TimeRangePreset;
	/**
	 * The explicit window when `timeRangePreset` is `'custom'`, as ISO 8601 strings (not
	 * `Date`s, unlike Logs/Metrics, so this shape stays its own saved-view payload). Only
	 * reachable through a link - the External APIs page's chart click - since the toolbar
	 * offers presets only; picking one clears it. `null` otherwise.
	 */
	customRange: { from: string; to: string } | null;
	services: string[];
	/** User-built attribute filters (SpanAttributeFiltersRow.svelte's expandable builder section) - ANDed together, same shape LogsFilterState.attributeFilters documents for Logs. */
	attributeFilters: SpanAttributeFilter[];
	/** Root-span status labels (`STATUS_CODE_*`), any of - set from the facet sidebar. */
	statusCodes: string[];
	/** Root-span OTel SpanKind numbers, any of - set from the facet sidebar. */
	kinds: number[];
	/** Root-span names (the operation), any of - set from the facet sidebar. */
	names: string[];
	/** Lower bound (ns) of the facet sidebar's selected duration bucket, `null` = any - see `$lib/traces/duration-buckets.ts`. */
	durationBucketNano: number | null;
	/**
	 * List each service's entry spans (no parent, or a parent in another service) instead of
	 * one root span per trace - see `SpanFilter.EntrySpansOnly` (SpanFilter.cs). A list mode
	 * rather than a content filter, so "Clear filters" keeps it, same as the time range.
	 */
	entrySpansOnly: boolean;
	/** List order (TraceList's clickable column headers) - like `entrySpansOnly`, a list setting "Clear filters" keeps. */
	sortBy: SpanSortKey;
	sortAscending: boolean;
	/**
	 * The applied structural query (TraceStructureEditor.svelte), `null` = none - only
	 * traces whose span tree matches it are listed. See `SpanFilter.Structure` (SpanFilter.cs).
	 */
	structure: TraceStructureFilter | null;
	/** Only traces containing an N+1 pattern - see `SpanFilter.NPlusOneOnly` (SpanFilter.cs). A content filter, so "Clear filters" resets it. */
	nPlusOneOnly: boolean;
}

/** A saved view's `state` payload for `pageType: 'Traces'` - identical to `TracesFilterState` (no `Date`-typed fields here, unlike Logs' `customRange`, so no separate serialized shape is needed). The facet fields and `customRange` are optional: views saved before they existed simply lack them. */
type OptionalSavedField = 'customRange' | 'statusCodes' | 'kinds' | 'names' | 'durationBucketNano' | 'entrySpansOnly' | 'sortBy' | 'sortAscending' | 'structure' | 'nPlusOneOnly';
export type TracesSavedViewState = Omit<TracesFilterState, OptionalSavedField> & Partial<Pick<TracesFilterState, OptionalSavedField>>;

function emptyFilter(timeRangePreset: TimeRangePreset, services: string[] = [], attributeFilters: SpanAttributeFilter[] = []): TracesFilterState {
	return { timeRangePreset, customRange: null, services, attributeFilters, statusCodes: [], kinds: [], names: [], durationBucketNano: null, entrySpansOnly: false, sortBy: 'StartTime', sortAscending: false, structure: null, nPlusOneOnly: false };
}

export class TracesExplorerState {
	filter = $state<TracesFilterState>(emptyFilter(regional.defaultRanges.traces));

	// One row per trace (root spans), or per service request in `entrySpansOnly` mode -
	// see `rowKey`. Never mutated in place, always a wholesale
	// reassignment (fresh search or page append), same $state.raw rationale
	// LogsExplorerState documents for its own `events` field.
	traces = $state.raw<SpanDto[]>([]);
	nextCursor = $state<string | null>(null);

	loading = $state(false);
	/** "Did you mean..." case-variant replacements for an empty result - see `$lib/case-suggestions.ts`. */
	caseSuggestions = $state.raw<CaseSuggestion[]>([]);
	loadingMore = $state(false);
	error = $state<string | null>(null);

	// No dedicated "list distinct services" endpoint for spans either (same gap
	// LogsExplorerState.knownServices documents for logs, worked around there via a
	// broad aggregate query) - spans has no /api/spans/aggregate yet (deferred as a
	// fast-follow, not required for this roadmap slice), so this instead derives known
	// services from a broad, wide-window search's results. Good enough for a picklist;
	// not a guarantee of completeness.
	knownServices = $state.raw<string[]>([]);

	/** Toolbar-driven "Auto-refresh" toggle (roadmap: "Auto-refresh toggle for the Traces
	 *  and Metrics explorer pages") - re-runs `runSearch()` on `AUTO_REFRESH_INTERVAL_MS`
	 *  while on, same "poll while enabled" shape ServicesState.startPolling already uses.
	 *  Deliberately not part of `TracesFilterState`/the saved-view payload - it's a
	 *  per-session UI preference ("keep this open and watch it"), not part of what a saved
	 *  view reproduces, same call Logs' own `live` field makes for itself. */
	autoRefreshEnabled = $state(false);

	/** Whether TraceStructureEditor.svelte is shown - UI only, not saved (the applied `filter.structure` is). */
	structureEditorOpen = $state(false);

	#seenRowKeys = new Set<string>();
	#searchAbort: AbortController | null = null;
	#autoRefreshHandle: ReturnType<typeof setInterval> | null = null;

	/**
	 * Dedupes a freshly-fetched page against `#seenTraceIds` (cross-page duplicates) *and*
	 * against itself (same-page duplicates) - see LogsExplorerState's identically-shaped
	 * `#dedupeAgainstSeen`, which this mirrors, for why the naive `!seenIds.has` filter
	 * alone isn't enough: two same-ID rows arriving in the same `res.spans` array both pass
	 * it (neither is in `seenTraceIds` *yet*), landing in `traces` with an identical key -
	 * exactly what trips Svelte's each_key_duplicate on the keyed {#each} in VirtualList.
	 */
	#dedupeAgainstSeen(spans: SpanDto[]): SpanDto[] {
		const fresh: SpanDto[] = [];
		for (const s of spans) {
			const key = this.rowKey(s);
			if (this.#seenRowKeys.has(key)) continue;
			this.#seenRowKeys.add(key);
			fresh.push(s);
		}
		return fresh;
	}

	/**
	 * A `traces` row's identity - the trace id in root-span mode (one row per trace), the
	 * span too in `entrySpansOnly` mode, where one trace legitimately yields a row per
	 * service it passed through.
	 */
	rowKey(span: SpanDto): string {
		return this.filter.entrySpansOnly ? `${span.traceId}:${span.spanId}` : span.traceId;
	}

	/**
	 * @param overrides.attributeFilters Substituted for `this.filter.attributeFilters` when
	 * given - see LogsExplorerState.buildFilter's identically-shaped parameter for why
	 * (SpanAttributeFiltersRow's value autocomplete needs the same "exclude the row being
	 * typed" exclusion).
	 * @param overrides.rootSpansOnly Defaults to `true` (this page's own trace-list search
	 * wants root spans - see `SpanDto.SpanCount`'s remarks - or, with `filter.entrySpansOnly`,
	 * entry spans instead; `false` drops both). SpanAttributeFiltersRow's
	 * value autocomplete overrides it to `false`: live-verified an attribute like
	 * `peer.service` typically lives on a *child* span (an outbound call a root span like
	 * `handle-request` merely triggers, not one it carries itself), so leaving this `true`
	 * silently searched zero matching rows for exactly the attributes autocomplete exists
	 * to help with.
	 */
	buildFilter(range: ResolvedTimeRange | null, overrides?: { attributeFilters?: SpanAttributeFilter[]; rootSpansOnly?: boolean }): SpanFilter {
		const scoped = overrides?.rootSpansOnly ?? true;
		const filter: SpanFilter = scoped && this.filter.entrySpansOnly ? { entrySpansOnly: true } : { rootSpansOnly: scoped };
		if (range) {
			filter.from = range.from;
			filter.to = range.to;
		}
		if (this.filter.services.length) filter.services = [...this.filter.services];
		if (this.filter.statusCodes.length) filter.statusCodes = [...this.filter.statusCodes];
		if (this.filter.kinds.length) filter.kinds = [...this.filter.kinds];
		if (this.filter.names.length) filter.names = [...this.filter.names];
		if (this.filter.durationBucketNano !== null) {
			const { min, max } = durationBucketRange(this.filter.durationBucketNano);
			filter.minDurationNano = min;
			if (max !== undefined) filter.maxDurationNano = max;
		}
		const attributes = overrides?.attributeFilters ?? this.filter.attributeFilters;
		if (attributes.length) filter.attributes = [...attributes];
		if (this.filter.structure) filter.structure = this.filter.structure;
		if (this.filter.nPlusOneOnly) filter.nPlusOneOnly = true;
		return filter;
	}

	#resolvedRange(): ResolvedTimeRange | null {
		const custom = this.filter.customRange;
		return resolveTimeRange(this.filter.timeRangePreset, custom ? { from: new Date(custom.from), to: new Date(custom.to) } : undefined);
	}

	/** Public wrapper around #resolvedRange - same "the window currently in scope" rationale as LogsExplorerState.currentRange. Used by SpanAttributeFiltersRow's value autocomplete to scope its suggestions to what's actually being searched. */
	currentRange(): ResolvedTimeRange | null {
		return this.#resolvedRange();
	}

	/** One-off, wide-window (7d) root-span search just to enumerate service names. */
	async loadKnownServices(): Promise<void> {
		try {
			const to = new Date();
			const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
			const res = await searchSpans({
				filter: { rootSpansOnly: true, from: from.toISOString(), to: to.toISOString() },
				pageSize: 500
			});
			this.knownServices = [...new Set(res.spans.map((s) => s.serviceName).filter(Boolean))].sort();
		} catch {
			// Non-critical - the service filter just shows fewer/no options until a retry.
		}
	}

	#sort(): { sortBy: SpanSortKey; sortAscending: boolean } {
		return { sortBy: this.filter.sortBy, sortAscending: this.filter.sortAscending };
	}

	async runSearch(): Promise<void> {
		this.#searchAbort?.abort();
		const abort = new AbortController();
		this.#searchAbort = abort;

		this.loading = true;
		this.error = null;
		this.caseSuggestions = [];
		try {
			const res = await searchSpans({ filter: this.buildFilter(this.#resolvedRange()), pageSize: PAGE_SIZE, ...this.#sort() }, abort.signal);
			if (abort.signal.aborted) return;
			this.#seenRowKeys = new Set();
			this.traces = this.#dedupeAgainstSeen(res.spans);
			this.nextCursor = res.nextCursor;
			if (this.traces.length === 0) void this.#loadCaseSuggestions(abort.signal);
		} catch (err) {
			if (abort.signal.aborted) return;
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.loading = false;
		}
	}

	/** Best-effort: only runs for an empty result with user-built Equals/In attribute filters; failures leave no suggestions. */
	async #loadCaseSuggestions(signal: AbortSignal): Promise<void> {
		const filters = this.filter.attributeFilters;
		if (filters.length === 0) return;
		const range = this.#resolvedRange();
		const suggestions = await findCaseSuggestions(filters, async (index, prefix, limit) => {
			const rest = filters.filter((_, i) => i !== index);
			const f = filters[index];
			const res = await getSpanAttributeValues({ filter: this.buildFilter(range, { attributeFilters: rest }), bag: f.bag, key: f.key, prefix, limit }, signal);
			return res.values;
		});
		if (!signal.aborted) this.caseSuggestions = suggestions;
	}

	/** Swaps the mistyped-case value for the suggested one and re-runs the search. */
	applyCaseSuggestion(s: CaseSuggestion): void {
		this.setAttributeFilters(applyCaseSuggestionTo(this.filter.attributeFilters, s));
	}

	async loadMore(): Promise<void> {
		if (!this.nextCursor || this.loadingMore) return;
		this.loadingMore = true;
		try {
			const res = await searchSpans({
				filter: this.buildFilter(this.#resolvedRange()),
				cursor: this.nextCursor,
				pageSize: PAGE_SIZE,
				...this.#sort()
			});
			const fresh = this.#dedupeAgainstSeen(res.spans);
			this.traces = [...this.traces, ...fresh];
			this.nextCursor = res.nextCursor;
		} catch (err) {
			this.error = err instanceof Error ? err.message : String(err);
		} finally {
			this.loadingMore = false;
		}
	}

	setAutoRefreshEnabled(enabled: boolean): void {
		if (enabled === this.autoRefreshEnabled) return;
		this.autoRefreshEnabled = enabled;
		if (enabled) this.#startAutoRefresh();
		else this.#stopAutoRefresh();
	}

	#startAutoRefresh(): void {
		this.#stopAutoRefresh();
		this.#autoRefreshHandle = setInterval(() => void this.runSearch(), AUTO_REFRESH_INTERVAL_MS);
	}

	#stopAutoRefresh(): void {
		if (this.#autoRefreshHandle !== null) {
			clearInterval(this.#autoRefreshHandle);
			this.#autoRefreshHandle = null;
		}
	}

	setTimeRangePreset(preset: TimeRangePreset): void {
		this.filter.timeRangePreset = preset;
		if (preset !== 'custom') this.filter.customRange = null;
		void this.runSearch();
	}

	/** Lands the filter on an explicit [from, to) range - the "Around a time" picker. */
	setCustomRange(range: { from: Date; to: Date }): void {
		this.filter.timeRangePreset = 'custom';
		this.filter.customRange = { from: range.from.toISOString(), to: range.to.toISOString() };
		void this.runSearch();
	}

	setServices(services: string[]): void {
		this.filter.services = services;
		void this.runSearch();
	}

	/** Wholesale-replaces the user-built attribute filters (SpanAttributeFiltersRow.svelte) - same "one setter, caller passes the full next array" shape as LogsExplorerState.setAttributeFilters. */
	setAttributeFilters(attributeFilters: SpanAttributeFilter[]): void {
		this.filter.attributeFilters = attributeFilters;
		void this.runSearch();
	}

	setStatusCodes(statusCodes: string[]): void {
		this.filter.statusCodes = statusCodes;
		void this.runSearch();
	}

	setKinds(kinds: number[]): void {
		this.filter.kinds = kinds;
		void this.runSearch();
	}

	setNames(names: string[]): void {
		this.filter.names = names;
		void this.runSearch();
	}

	setDurationBucketNano(lowerBoundNano: number | null): void {
		this.filter.durationBucketNano = lowerBoundNano;
		void this.runSearch();
	}

	/** Applies (or, with `null`, removes) the structural query - TraceStructureEditor.svelte's Apply/Remove buttons. */
	setStructure(structure: TraceStructureFilter | null): void {
		this.filter.structure = structure;
		void this.runSearch();
	}

	setNPlusOneOnly(nPlusOneOnly: boolean): void {
		if (nPlusOneOnly === this.filter.nPlusOneOnly) return;
		this.filter.nPlusOneOnly = nPlusOneOnly;
		void this.runSearch();
	}

	setEntrySpansOnly(entrySpansOnly: boolean): void {
		if (entrySpansOnly === this.filter.entrySpansOnly) return;
		this.filter.entrySpansOnly = entrySpansOnly;
		void this.runSearch();
	}

	/**
	 * A column header click: the active column flips direction; any other column becomes
	 * active, descending first (newest / slowest / most spans - the useful end of each).
	 */
	toggleSort(sortBy: SpanSortKey): void {
		if (this.filter.sortBy === sortBy) {
			this.filter.sortAscending = !this.filter.sortAscending;
		} else {
			this.filter.sortBy = sortBy;
			this.filter.sortAscending = false;
		}
		void this.runSearch();
	}

	/** Whether the toolbar's "Clear filters" button has anything to do - same fields `resetFilters` zeroes out. */
	hasActiveFilters(): boolean {
		return (
			this.filter.services.length > 0 ||
			this.filter.attributeFilters.length > 0 ||
			this.filter.statusCodes.length > 0 ||
			this.filter.kinds.length > 0 ||
			this.filter.names.length > 0 ||
			this.filter.durationBucketNano !== null ||
			this.filter.structure !== null ||
			this.filter.nPlusOneOnly
		);
	}

	/**
	 * Toolbar's "Clear filters" button - every content filter (services, attribute
	 * filters, the facet sidebar's status/kind/operation/duration), leaving the time range
	 * alone - same scope LogsExplorerState.resetFilters documents for itself.
	 */
	resetFilters(): void {
		this.filter = {
			...emptyFilter(this.filter.timeRangePreset),
			customRange: this.filter.customRange,
			entrySpansOnly: this.filter.entrySpansOnly,
			sortBy: this.filter.sortBy,
			sortAscending: this.filter.sortAscending
		};
		void this.runSearch();
	}

	/** Serializes the current filter into a saved view's opaque `state` payload - see `TracesSavedViewState`. */
	toSavedViewState(): TracesSavedViewState {
		return {
			timeRangePreset: this.filter.timeRangePreset,
			customRange: this.filter.customRange ? { ...this.filter.customRange } : null,
			services: [...this.filter.services],
			attributeFilters: this.filter.attributeFilters.map((a) => ({ ...a })),
			statusCodes: [...this.filter.statusCodes],
			kinds: [...this.filter.kinds],
			names: [...this.filter.names],
			durationBucketNano: this.filter.durationBucketNano,
			entrySpansOnly: this.filter.entrySpansOnly,
			sortBy: this.filter.sortBy,
			sortAscending: this.filter.sortAscending,
			structure: this.filter.structure ? cloneStructure(this.filter.structure) : null,
			nPlusOneOnly: this.filter.nPlusOneOnly
		};
	}

	/** Restores a saved view's filter (defensively narrowed - see `LogsExplorerState.applySavedViewState`'s identical caveat) and re-runs the search. */
	applySavedViewState(state: unknown): void {
		const s = (state ?? {}) as Partial<TracesSavedViewState>;
		// A 'custom' preset without a usable range would search with no time bound at all.
		const customRange = isCustomRange(s.customRange) ? { from: s.customRange.from, to: s.customRange.to } : null;
		const preset = s.timeRangePreset === 'custom' && !customRange ? '1h' : (s.timeRangePreset ?? '1h');
		this.filter = {
			...emptyFilter(preset, s.services ?? [], s.attributeFilters ?? []),
			customRange: preset === 'custom' ? customRange : null,
			statusCodes: s.statusCodes ?? [],
			kinds: s.kinds ?? [],
			names: s.names ?? [],
			durationBucketNano: typeof s.durationBucketNano === 'number' ? s.durationBucketNano : null,
			entrySpansOnly: s.entrySpansOnly === true,
			sortBy: s.sortBy && SPAN_SORT_KEYS.includes(s.sortBy) ? s.sortBy : 'StartTime',
			sortAscending: s.sortAscending === true,
			structure: isStructure(s.structure) ? cloneStructure(s.structure) : null,
			nPlusOneOnly: s.nPlusOneOnly === true
		};
		void this.runSearch();
	}

	/**
	 * Arrival filter for the "View traces" deep link from a Metrics chart (see
	 * MetricChart.svelte / `$lib/deep-links.ts`, and `traces/+page.svelte`'s onMount,
	 * the only caller). No patternId-style extra state to carry here, unlike Logs -
	 * attributeFilters is reset to empty, same "one-off hop, not something to persist and
	 * reproduce" reasoning `LogsExplorerState.applyDeepLinkFilter` documents for its own
	 * reset fields.
	 */
	applyDeepLinkFilter(params: { services: string[]; timeRangePreset: TimeRangePreset }): void {
		this.filter = emptyFilter(params.timeRangePreset, params.services);
		void this.runSearch();
	}

	dispose(): void {
		this.#searchAbort?.abort();
		this.#stopAutoRefresh();
	}
}

function cloneStructure(structure: TraceStructureFilter): TraceStructureFilter {
	return {
		expression: structure.expression,
		conditions: structure.conditions.map((c) => ({ ...c, attributes: c.attributes?.map((a) => ({ ...a })) }))
	};
}

/** A saved view's `customRange`, defensively narrowed - both ends must parse as dates. */
function isCustomRange(value: unknown): value is { from: string; to: string } {
	const v = value as { from?: unknown; to?: unknown } | null | undefined;
	return (
		v != null &&
		typeof v.from === 'string' &&
		typeof v.to === 'string' &&
		!Number.isNaN(Date.parse(v.from)) &&
		!Number.isNaN(Date.parse(v.to))
	);
}

/** A saved view's `structure`, defensively narrowed - views saved before it existed lack it. */
function isStructure(value: unknown): value is TraceStructureFilter {
	const v = value as Partial<TraceStructureFilter> | null | undefined;
	return v != null && typeof v.expression === 'string' && Array.isArray(v.conditions);
}
