// Central reactive state for the Metrics page - mirrors TracesExplorerState's shape
// (`$lib/traces/state.svelte.ts`): a class with `$state` fields, provided via
// `metricsExplorerContext` rather than passed as props. No live-tail here either, same
// "the chart is the value, not a firehose" reasoning traces/spans already established.
//
// Unlike Traces (one list, one detail view), Metrics has two coupled queries: the
// picker's name list (POST /api/metrics/names) and the chart's series data
// (POST /api/metrics/query) for whichever metric is currently selected - both refetch
// on every filter change, and the query additionally refetches on selection change.
// With `filter.compareEnabled` on, the series query becomes two parallel /query calls
// (current + previous period) - see runQuery's own remarks.

import {
	getMetricNames,
	getMetricAttributeKeys,
	queryMetric,
	type MetricAttributeKeyInfo,
	type MetricHavingOperator,
	type MetricNameInfo,
	type MetricPointType,
	type MetricPostProcessFunction,
	type MetricSeries
} from '$lib/metrics-api';
import { resolveTimeRange, rangeSeconds, previousPeriod, type TimeRangePreset, type ResolvedTimeRange } from '$lib/logs/time-range';
import { pickBucketWidthSeconds } from '$lib/logs/bucket-width';
import { parseFormula, evaluateFormula, collectRefs } from './formula';

export interface MetricsFilterState {
	timeRangePreset: TimeRangePreset;
	/** Set only while timeRangePreset === 'custom' - same shape/lifecycle as LogsFilterState.customRange. Unlike Logs, nothing in MetricsToolbar ever picks 'custom' directly (see MetricsToolbar's own remarks); the only producer is MetricChart's drag-to-zoom, via setCustomRange below. */
	customRange: { from: Date; to: Date } | null;
	services: string[];
	/** "Compare with previous period" toggle - see MetricChart.svelte's comparison-mode remarks. Unlike Logs' patternId/attribute drill-downs, this *is* carried in a saved view (toSavedViewState/applySavedViewState below) - it's a display preference for the metric, not a one-off hop. */
	compareEnabled: boolean;
	/** "Group by" attribute key (see MetricQueryRequest.groupByAttributeKey), or null when ungrouped. Same "real display preference, not a one-off hop" reasoning as compareEnabled - carried in a saved view too. */
	groupByAttributeKey: string | null;
	/** Max series to chart, ranked by magnitude - see MetricQueryRequest.topN's remarks and MetricsToolbar's "Top N" picker. Always a concrete number, never null: unlike groupByAttributeKey there's no "uncapped" option, only which cap - same "real display preference, not a one-off hop" reasoning as compareEnabled/groupByAttributeKey, carried in a saved view too. Defaults to DEFAULT_TOP_N, which mirrors (but doesn't import - no shared config between Flare.Api and the dashboard) MetricSeriesQueryBuilder.DefaultTopN. */
	topN: number;
	/**
	 * Post-aggregation filter over the same ranking magnitude `topN` caps by - "only series
	 * where the aggregated value exceeds X" (see `MetricQueryRequest.havingOperator`'s
	 * remarks and `MetricsToolbar`'s "Having" control). Both null together = no filter;
	 * unlike `topN` there genuinely is an "off" state here, so this stays nullable rather
	 * than defaulting to some always-on comparison. Same "real display preference, not a
	 * one-off hop" reasoning as `compareEnabled`/`groupByAttributeKey`/`topN` - carried in a
	 * saved view too.
	 */
	havingOperator: MetricHavingOperator | null;
	havingValue: number | null;
	/**
	 * App-side post-processing chain (ADR-0038, roadmap: "Per-query post-processing
	 * functions (metrics and logs)") applied, in order, to the queried series' points -
	 * see `MetricQueryRequest.postProcessFunctions`'s remarks and `MetricsToolbar`'s
	 * "Functions" control. Empty array = no post-processing. Same "real display
	 * preference, not a one-off hop" reasoning as `compareEnabled`/`groupByAttributeKey`/
	 * `topN` - carried in a saved view too. Ignored (and hidden in the toolbar) when the
	 * selected metric is a Histogram - see `MetricPostProcessor`'s remarks for why.
	 */
	postProcessFunctions: MetricPostProcessFunction[];
}

/** Mirrors `MetricSeriesQueryBuilder.DefaultTopN` on the API side - see `MetricsFilterState.topN`'s own remarks for why this can't just be imported instead. */
export const DEFAULT_TOP_N = 20;

/**
 * Metrics Explorer's two mutually-exclusive display modes - 'single' is everything this file
 * already did (one selected metric via `MetricPicker`/`selected`); 'formula' (roadmap:
 * "Cross-query formula expressions for metrics") layers on a second, independent set of
 * fields below rather than reusing `selected`/`series`/`resultType` - those are wired
 * throughout `MetricChart.svelte` around a single (metricName, serviceName, type) selection,
 * and a formula result has none of that (no one metric name/type/unit, possibly several
 * distinct (letter, series) inputs joined together) - see `FormulaChart.svelte`, a separate
 * component from `MetricChart.svelte` for the same reason.
 */
export type MetricsExplorerMode = 'single' | 'formula';

/** One named query row (`A`, `B`, ...) in Formula mode - a metric selection + its own optional Group by, same shape `MetricsFilterState.groupByAttributeKey` gives the single-metric mode, just per-row instead of page-wide. No TopN/Having here (v1 scope cut, see `FormulaExplorerState.runFormulaQuery`'s remarks) and no service filter beyond the metric's own `serviceName` - same "the picker entry already pins a (metricName, serviceName) pair" convention `runQuery`'s own `filterFor` uses for single mode. */
export interface FormulaQueryDef {
	letter: string;
	metric: MetricNameInfo | null;
	groupByAttributeKey: string | null;
	/** This query's own metric's attribute keys - fetched independently per row (unlike `knownAttributeKeys`, which tracks the single-mode `selected` metric), since each formula row can have a completely different metric with a completely different key set. */
	attributeKeys: MetricAttributeKeyInfo[];
	attributeKeysLoading: boolean;
}

/** Formula rows are lettered A, B, C, ... in definition order - a closed, small set (chart lines, not a general-purpose list) same "closed, sane set of choices" call `MetricsToolbar`'s `TOP_N_OPTIONS` already makes. 6 is generous for "combine two named metric queries" (the roadmap's own phrasing) without inviting an unreadable formula. */
export const MAX_FORMULA_QUERIES = 6;

const FORMULA_LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';

/** The first letter not already in use by `existing` - `FormulaQueryDef.letter` is assigned once at creation and never renumbered (removing query B while A/C exist leaves a gap, same as removing a mid-list saved item anywhere else in this codebase), so a fresh row always gets the lowest free letter rather than reusing a position. */
function nextFormulaLetter(existing: readonly FormulaQueryDef[]): string {
	const used = new Set(existing.map((q) => q.letter));
	for (const letter of FORMULA_LETTERS) {
		if (!used.has(letter)) return letter;
	}
	throw new Error('No formula letters left.');
}

function newFormulaQuery(letter: string): FormulaQueryDef {
	return { letter, metric: null, groupByAttributeKey: null, attributeKeys: [], attributeKeysLoading: false };
}

/**
 * A saved view's `state` payload for `pageType: 'Metrics'` - `MetricsFilterState` plus the
 * currently-charted metric's identity. Unlike Logs/Traces, "the view" on this page isn't
 * just the filter: which (metricName, serviceName) is selected is equally part of what a
 * saved view should reproduce, so it's carried alongside the filter here rather than
 * needing a second saved-view concept.
 *
 * `Omit<..., 'customRange'>` + its own re-declaration, not a plain `extends` - `customRange`
 * uses ISO strings here instead of `Date` objects, since `Date` doesn't survive a JSON
 * round-trip through Flare.Api's opaque `JsonElement` storage. Same reasoning
 * `LogsSavedViewState`'s own (standalone, not `extends`-based) declaration documents.
 */
export interface MetricsSavedViewState extends Omit<MetricsFilterState, 'customRange'> {
	customRange: { from: string; to: string } | null;
	selectedMetric: { metricName: string; serviceName: string; type: MetricPointType } | null;
	/** Omitted from older saved views (pre-dates Formula mode) - `applySavedViewState` defaults it to 'single', the only mode that existed then. */
	mode?: MetricsExplorerMode;
	formulaExpression?: string;
	/** `FormulaQueryDef` minus its transient `attributeKeys`/`attributeKeysLoading` fields, and `metric` narrowed to just its identity - same (metricName, serviceName, type) triple `selectedMetric` above already uses, re-resolved against `names` on restore rather than round-tripped as a full `MetricNameInfo` (unit/description/seriesCount could be stale by the time the view is reopened). */
	formulaQueries?: Array<{
		letter: string;
		metric: { metricName: string; serviceName: string; type: MetricPointType } | null;
		groupByAttributeKey: string | null;
	}>;
}

/** Identifies one picker entry/selection - a (metricName, serviceName) pair, since the same metric name can be emitted by more than one service (see MetricNameInfo.serviceName's C# doc comment). */
function metricKey(metric: Pick<MetricNameInfo, 'metricName' | 'serviceName'>): string {
	return `${metric.metricName} ${metric.serviceName}`;
}

/**
 * How long MetricChart's own out-transition takes when the displayed metric/mode
 * changes (its `FADE_MS`, imported from here rather than hand-picked separately in
 * both places - two independently-chosen numbers that happened to match would
 * drift the moment either one changed). `#deferredReset` below waits exactly this
 * long before actually clearing series/previousSeries/intervalSeconds on a metric
 * switch, so MetricChart's *outgoing* chart - which reads those fields live, not a
 * frozen snapshot - keeps rendering the metric it already had for the whole
 * fade-out instead of visibly jumping to "no data" the instant `selected` flips,
 * well before the fade-out even starts to play.
 */
export const METRIC_SWITCH_FADE_MS = 250;

/** How often `autoRefreshEnabled` re-runs `runQuery()` - see its own remarks. Same value TracesExplorerState.AUTO_REFRESH_INTERVAL_MS uses, for the same "one query per interval" shape. */
const AUTO_REFRESH_INTERVAL_MS = 30_000;

export class MetricsExplorerState {
	filter = $state<MetricsFilterState>({
		timeRangePreset: '1h',
		customRange: null,
		services: [],
		compareEnabled: false,
		groupByAttributeKey: null,
		topN: DEFAULT_TOP_N,
		havingOperator: null,
		havingValue: null,
		postProcessFunctions: []
	});

	// Never mutated in place, always a wholesale reassignment - same $state.raw
	// rationale TracesExplorerState.traces documents.
	names = $state.raw<MetricNameInfo[]>([]);
	namesLoading = $state(false);
	namesError = $state<string | null>(null);

	selected = $state<MetricNameInfo | null>(null);

	series = $state.raw<MetricSeries[]>([]);
	queryLoading = $state(false);
	queryError = $state<string | null>(null);
	// The previous-period series when filter.compareEnabled is on - fetched alongside
	// `series` in runQuery (see there), same shape, one period earlier. Deliberately
	// fails soft: a broken/empty previous fetch just means "no previous data" to
	// MetricChart (its comparison lines/percentage degrade gracefully - see there),
	// never blocks `series`/queryError for the period that actually matters.
	previousSeries = $state.raw<MetricSeries[]>([]);
	// selected?.type *as of the query that produced the current series* -
	// deliberately not the same as selected?.type itself, which (unlike series/
	// intervalSeconds) is never deferred by #deferredReset - the header/picker
	// highlight update immediately on a metric switch, only the data lags. Without
	// this, MetricChart's isHistogram/isSum (which decide how `series` gets
	// interpreted - buildLines' Histogram branch vs. its Gauge/Sum one) would flip
	// to the *new* metric's type the instant `selected` changes, while `series`
	// still held the *old* metric's (differently-shaped) points for the whole
	// METRIC_SWITCH_FADE_MS deferral - misinterpreting one type's points as the
	// other's mid-outro, not just showing stale-but-correctly-shaped data. Set
	// alongside series/previousSeries/intervalSeconds everywhere they change, so
	// it's always describing what `series` actually *is*, never what's merely
	// targeted next.
	resultType = $state<MetricPointType | null>(null);
	// filter.compareEnabled *as of the query that produced the current series/
	// previousSeries* - deliberately not the same as filter.compareEnabled itself,
	// which flips the instant the toolbar switch is clicked, well before the matching
	// (re)fetch resolves. MetricChart keys its Current/Previous-vs-per-series line
	// mode off this, not the live filter, so the chart only ever switches shape in the
	// same paint the data for that shape actually arrives - no frame where the toggle
	// reads "on" but the previous-period line hasn't loaded yet (a real, shipped
	// "blink" this field exists to fix, not a hypothetical one).
	resultCompareEnabled = $state(false);
	// The bucketWidthSeconds actually sent with the current `series` - for the chart
	// header's "1m interval" metadata row. Only reset (along with series/previousSeries/
	// queryError - see #resetForNewMetric) when the *selected metric itself* changes,
	// never on a plain filter refetch (time range, service, compare toggle) - those keep
	// showing the last good chart/metadata row until the new data arrives and replaces
	// it in one go, same "stale-while-revalidate, never an empty gap" reasoning
	// resultCompareEnabled above documents. A metric switch still resets immediately:
	// two different metrics can have completely different intervals/units, so briefly
	// showing metric A's chart under metric B's name would be actively misleading in a
	// way a same-metric refetch reusing slightly-stale data never is.
	intervalSeconds = $state<number | null>(null);

	// The exact [from, to) range actually sent with the current `series`/`previousSeries` -
	// same "requested range, not bucket extremes or a freshly re-resolved 'now'" reasoning
	// VolumeChart.svelte's own rangeFrom/rangeTo document, and the same reset/staleness
	// rules as intervalSeconds above (only cleared on a real metric switch, not a plain
	// filter refetch). MetricChart's drag-to-zoom reads these to map a pointer's fractional
	// x-position back to a concrete instant - re-resolving a fixed preset via
	// resolveTimeRange() at drag time instead would drift from what's actually plotted by
	// however long it's been since the last query (autoRefreshEnabled polls every 30s), a
	// small but real skew this sidesteps entirely.
	queryRangeFrom = $state<string | null>(null);
	queryRangeTo = $state<string | null>(null);

	// Not derived from `names` (which is already narrowed by the current service
	// filter - self-narrowing the picklist as soon as one service is chosen, same
	// chicken-and-egg problem TracesExplorerState.knownServices' own comment
	// documents). A separate, wide-window (7d), unfiltered-by-service discovery call
	// instead, same workaround.
	knownServices = $state.raw<string[]>([]);

	// Attribute keys available for the *currently selected metric* - unlike
	// knownServices' deliberately wide/decoupled 7-day scope (avoiding a
	// self-narrowing picklist), grouping by a key that doesn't exist on this metric is
	// meaningless, so this is intentionally narrow: this metric's own keys, refreshed
	// on every metric switch (see #deferredReset below).
	knownAttributeKeys = $state.raw<MetricAttributeKeyInfo[]>([]);
	knownAttributeKeysLoading = $state(false);
	knownAttributeKeysError = $state<string | null>(null);

	/** Toolbar-driven "Auto-refresh" toggle (roadmap: "Auto-refresh toggle for the Traces
	 *  and Metrics explorer pages") - re-runs `runQuery()` for the currently selected
	 *  metric on `AUTO_REFRESH_INTERVAL_MS`, same "poll while enabled" shape
	 *  TracesExplorerState.autoRefreshEnabled uses. Deliberately not part of
	 *  `MetricsFilterState`/the saved-view payload - see that field's own remarks on why
	 *  this is a per-session preference, not something a saved view reproduces. Doesn't
	 *  re-run `loadNames()` - "the current query" (the roadmap's own words) is the chart's
	 *  series query, not the picker's name list, which changes far less often. */
	autoRefreshEnabled = $state(false);

	// ---- Formula mode (see MetricsExplorerMode's own remarks for why this is a fully
	// separate set of fields rather than reusing series/resultType/selected above). ----

	mode = $state<MetricsExplorerMode>('single');

	formulaQueries = $state<FormulaQueryDef[]>([newFormulaQuery('A'), newFormulaQuery('B')]);
	formulaExpression = $state('A / B');
	/** Set from `parseFormula` on every `setFormulaExpression` call - a parse error blocks `runFormulaQuery` entirely (there's no partial/best-effort formula to run), same "surface it, don't guess" choice `MetricSeriesQueryBuilder`'s own required fields make. */
	formulaExpressionError = $state<string | null>(null);

	formulaSeries = $state.raw<MetricSeries[]>([]);
	formulaLoading = $state(false);
	/** A hard failure - a query fetch rejected, or a referenced letter has no query/metric. Distinct from `formulaWarning` (a valid-but-empty result) the same way `queryError` and an empty `series` are distinct in single mode. */
	formulaError = $state<string | null>(null);
	/** A non-fatal `evaluateFormula` warning (e.g. the join produced zero series) - shown as a hint alongside a valid-but-empty chart, never blocking like `formulaError` does. */
	formulaWarning = $state<string | null>(null);
	formulaIntervalSeconds = $state<number | null>(null);
	formulaRangeFrom = $state<string | null>(null);
	formulaRangeTo = $state<string | null>(null);

	#namesAbort: AbortController | null = null;
	#queryAbort: AbortController | null = null;
	#attributeKeysAbort: AbortController | null = null;
	#formulaQueryAbort: AbortController | null = null;
	#formulaAttributeKeysAborts = new Map<string, AbortController>();
	#pendingSwitchTimeout: ReturnType<typeof setTimeout> | null = null;
	#autoRefreshHandle: ReturnType<typeof setInterval> | null = null;

	#resolvedRange(): ResolvedTimeRange {
		// MetricsToolbar's own preset picker still never offers 'custom' directly (same as
		// TracesToolbar - only fixed-duration presets make sense for a manual pick, see its
		// own remarks), but MetricChart's drag-to-zoom (setCustomRange below) can land the
		// filter on 'custom' with a concrete range, same as Logs. The '1h' fallback stays a
		// defensive default for the truly unreachable case (a stray 'custom' with no range
		// ever attached), never expected to be hit in practice.
		return resolveTimeRange(this.filter.timeRangePreset, this.filter.customRange ?? undefined) ?? resolveTimeRange('1h')!;
	}

	#servicesOrUndefined(): string[] | undefined {
		return this.filter.services.length ? [...this.filter.services] : undefined;
	}

	/** One-off, wide-window (7d) unfiltered names call just to enumerate service names. */
	async loadKnownServices(): Promise<void> {
		try {
			const to = new Date();
			const from = new Date(to.getTime() - 7 * 24 * 60 * 60 * 1000);
			const res = await getMetricNames({ from: from.toISOString(), to: to.toISOString() });
			this.knownServices = [...new Set(res.metrics.map((m) => m.serviceName).filter(Boolean))].sort();
		} catch {
			// Non-critical - the service filter just shows fewer/no options until a retry.
		}
	}

	/**
	 * Loads the "Group by" picker's option list for the currently selected metric - see
	 * knownAttributeKeys' own remarks for why this is narrowly scoped, unlike
	 * loadKnownServices. Called from #deferredReset (immediately, not deferred by its
	 * fade timeout - this drives the toolbar picker, not the chart) whenever `selected`
	 * is set, covering selectMetric, loadNames' auto-select fallback, and
	 * applySavedViewState (which goes through selectMetric).
	 *
	 * If the currently-chosen filter.groupByAttributeKey doesn't exist on the new
	 * metric's key list, resets it to null and re-runs the query - covers a metric
	 * switch landing on a metric without that key, and a saved view restoring a key that
	 * no longer exists.
	 */
	async loadKnownAttributeKeys(): Promise<void> {
		this.#attributeKeysAbort?.abort();

		if (!this.selected) {
			this.knownAttributeKeys = [];
			return;
		}

		const abort = new AbortController();
		this.#attributeKeysAbort = abort;
		const metric = this.selected;

		this.knownAttributeKeysLoading = true;
		this.knownAttributeKeysError = null;
		try {
			const range = this.#resolvedRange();
			const res = await getMetricAttributeKeys(
				{
					metricName: metric.metricName,
					type: metric.type,
					filter: { from: range.from, to: range.to, services: [metric.serviceName] }
				},
				abort.signal
			);
			if (abort.signal.aborted) return;
			this.knownAttributeKeys = res.keys;

			const current = this.filter.groupByAttributeKey;
			if (current && !res.keys.some((k) => k.key === current)) {
				this.filter.groupByAttributeKey = null;
				void this.runQuery();
			}
		} catch (err) {
			if (abort.signal.aborted) return;
			this.knownAttributeKeysError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.knownAttributeKeysLoading = false;
		}
	}

	async loadNames(): Promise<void> {
		this.#namesAbort?.abort();
		const abort = new AbortController();
		this.#namesAbort = abort;

		this.namesLoading = true;
		this.namesError = null;
		try {
			const range = this.#resolvedRange();
			const res = await getMetricNames(
				{ from: range.from, to: range.to, services: this.#servicesOrUndefined() },
				abort.signal
			);
			if (abort.signal.aborted) return;
			this.names = res.metrics;

			// Keep the current selection if it's still in scope after the filter change;
			// otherwise fall back to the first available metric so the chart isn't blank
			// on first load or after narrowing the filter past the previous selection.
			const stillInScope = this.selected && this.names.some((m) => metricKey(m) === metricKey(this.selected!));
			if (!stillInScope) {
				this.selected = this.names[0] ?? null;
				this.#deferredReset();
			}
		} catch (err) {
			if (abort.signal.aborted) return;
			this.namesError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.namesLoading = false;
		}
	}

	/**
	 * Clears series/previousSeries/intervalSeconds/queryError back to their "nothing
	 * loaded yet" defaults - called right before runQuery whenever `selected` is about
	 * to point at a genuinely different metric (selectMetric, and loadNames' own
	 * auto-select fallback), never on a plain filter refetch of the *same* metric - see
	 * intervalSeconds' own remarks for why that distinction matters.
	 */
	#resetForNewMetric(): void {
		this.series = [];
		this.previousSeries = [];
		this.intervalSeconds = null;
		this.queryRangeFrom = null;
		this.queryRangeTo = null;
		this.queryError = null;
		this.resultType = null;
	}

	/**
	 * `#resetForNewMetric` + `runQuery`, but not until METRIC_SWITCH_FADE_MS from now -
	 * see that constant's own remarks for why the delay exists at all. `selected`
	 * itself is set by the caller *before* calling this (so the header/picker
	 * highlight update immediately - only the data clear is deferred).
	 */
	#deferredReset(): void {
		this.#flushPendingSwitch();
		// Not deferred by the fade timeout below - this drives the toolbar's "Group by"
		// picker, not the chart, so it can refresh as soon as `selected` changes instead
		// of waiting out MetricChart's outro.
		void this.loadKnownAttributeKeys();
		this.#pendingSwitchTimeout = setTimeout(() => {
			this.#pendingSwitchTimeout = null;
			this.#resetForNewMetric();
			void this.runQuery();
		}, METRIC_SWITCH_FADE_MS);
	}

	/**
	 * Runs a pending `#deferredReset` immediately instead of waiting out its timer -
	 * called by every other query-triggering setter (time range, services, compare),
	 * since any of them is about to run its own `runQuery` anyway, superseding
	 * whatever the deferred metric-switch reset was waiting to do. Without this, a
	 * filter change made within METRIC_SWITCH_FADE_MS of a metric switch could race
	 * its own fresh data against the deferred reset clearing it back out again.
	 */
	#flushPendingSwitch(): void {
		if (!this.#pendingSwitchTimeout) return;
		clearTimeout(this.#pendingSwitchTimeout);
		this.#pendingSwitchTimeout = null;
		this.#resetForNewMetric();
	}

	async runQuery(): Promise<void> {
		this.#queryAbort?.abort();

		if (!this.selected) {
			this.#resetForNewMetric();
			return;
		}

		const abort = new AbortController();
		this.#queryAbort = abort;
		const metric = this.selected;
		const compareEnabled = this.filter.compareEnabled;
		const groupByAttributeKey = this.filter.groupByAttributeKey ?? undefined;
		const topN = this.filter.topN;
		// Both-or-neither at the wire boundary too (see MetricsFilterState.havingOperator's
		// remarks) - a lone havingValue with no operator (or vice versa) would otherwise send
		// a half-set pair the server silently ignores anyway (MetricSeriesQueryBuilder.Build's
		// own remarks), but resolving it here keeps that invariant visible at the one call
		// site that actually crosses into the API layer.
		const havingOperator = this.filter.havingOperator && this.filter.havingValue != null ? this.filter.havingOperator : undefined;
		const havingValue = this.filter.havingOperator && this.filter.havingValue != null ? this.filter.havingValue : undefined;
		// Histogram has no single scalar Value to transform (MetricPostProcessor's own
		// remarks) - MetricsToolbar already hides the control for it, but this is the one
		// call site that actually crosses into the API layer, so the exclusion is enforced
		// here too rather than trusting the toolbar alone.
		const postProcessFunctions =
			metric.type !== 'Histogram' && this.filter.postProcessFunctions.length > 0 ? this.filter.postProcessFunctions : undefined;

		// Deliberately doesn't touch series/previousSeries/intervalSeconds here - only
		// queryError, and only because a stale error message next to fresh-looking
		// loading state would read wrong. Everything else stays exactly as it was
		// (stale-while-revalidate) until the try block below actually has something new
		// to replace it with - see intervalSeconds/resultCompareEnabled's own remarks.
		this.queryLoading = true;
		this.queryError = null;
		try {
			const range = this.#resolvedRange();
			const bucketWidthSeconds = pickBucketWidthSeconds(rangeSeconds(range));
			const filterFor = (r: ResolvedTimeRange) => ({ from: r.from, to: r.to, services: [metric.serviceName] });

			// Same bucketWidthSeconds for both, not re-picked from the previous range's own
			// duration (which would happen to match anyway, same duration) - explicit is
			// simpler to reason about than "trust it comes out the same". Previous is
			// best-effort (.catch, not awaited through the outer try/catch) - see
			// previousSeries' own remarks on why a broken previous fetch never blocks
			// `series`, the period that actually matters. Run in parallel, not sequenced,
			// so compare mode doesn't just double the wait.
			const [current, previous] = await Promise.all([
				queryMetric(
					{
						metricName: metric.metricName,
						type: metric.type,
						bucketWidthSeconds,
						filter: filterFor(range),
						groupByAttributeKey,
						topN,
						havingOperator,
						havingValue,
						postProcessFunctions
					},
					abort.signal
				),
				compareEnabled
					? queryMetric(
							{
								metricName: metric.metricName,
								type: metric.type,
								bucketWidthSeconds,
								filter: filterFor(previousPeriod(range)),
								groupByAttributeKey,
								topN,
								havingOperator,
								havingValue,
								postProcessFunctions
							},
							abort.signal
						).catch(() => null)
					: Promise.resolve(null)
			]);
			if (abort.signal.aborted) return;
			this.series = current.series;
			this.previousSeries = previous?.series ?? [];
			this.intervalSeconds = bucketWidthSeconds;
			this.queryRangeFrom = range.from;
			this.queryRangeTo = range.to;
			this.resultCompareEnabled = compareEnabled;
			this.resultType = metric.type;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.queryError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.queryLoading = false;
		}
	}

	selectMetric(metric: MetricNameInfo): void {
		if (this.selected && metricKey(this.selected) === metricKey(metric)) return;
		this.selected = metric;
		this.#deferredReset();
	}

	setTimeRangePreset(preset: TimeRangePreset): void {
		this.#flushPendingSwitch();
		this.filter.timeRangePreset = preset;
		// Picking a real preset (from MetricsToolbar's Select, which never offers 'custom'
		// itself) supersedes whatever drag-to-zoom range was active - same "leaving custom
		// clears it" rule LogsExplorerState.setTimeRangePreset follows, so a stale
		// customRange never lingers behind a fixed preset that no longer uses it.
		if (preset !== 'custom') this.filter.customRange = null;
		void this.loadNames();
		this.#runActive();
	}

	/**
	 * Lands the filter on an explicit [from, to) range - the only way MetricsExplorerState
	 * ever reaches `timeRangePreset === 'custom'` (MetricsToolbar's picker deliberately
	 * never offers it directly, see its own remarks). Called by MetricChart's drag-to-zoom,
	 * the same entry point VolumeChart's own drag gesture uses via
	 * LogsExplorerState.setCustomRange. Comparison mode keeps working on a custom range -
	 * buildComparisonLines/previousPeriod derive "one period back" from the resolved
	 * range's own duration, not a preset lookup, so an arbitrary dragged span still has a
	 * well-defined previous period to compare against.
	 */
	setCustomRange(range: { from: Date; to: Date }): void {
		this.#flushPendingSwitch();
		this.filter.timeRangePreset = 'custom';
		this.filter.customRange = range;
		void this.loadNames();
		this.#runActive();
	}

	setServices(services: string[]): void {
		this.#flushPendingSwitch();
		this.filter.services = services;
		void this.loadNames();
		this.#runActive();
	}

	/** No name-list reload needed, unlike setTimeRangePreset/setServices - which metrics exist doesn't depend on compare mode, only the chart's own query does. */
	setCompareEnabled(enabled: boolean): void {
		this.#flushPendingSwitch();
		this.filter.compareEnabled = enabled;
		void this.runQuery();
	}

	/** Same shape as setCompareEnabled - no name-list reload, which metrics exist doesn't depend on the grouping key, only the chart's own query does. */
	setGroupByAttribute(key: string | null): void {
		this.#flushPendingSwitch();
		this.filter.groupByAttributeKey = key;
		void this.runQuery();
	}

	/** Same shape as setGroupByAttribute - no name-list reload, which metrics exist doesn't depend on the series cap, only the chart's own query does. */
	setTopN(topN: number): void {
		this.#flushPendingSwitch();
		this.filter.topN = topN;
		void this.runQuery();
	}

	/**
	 * Sets or clears the post-aggregation `HAVING` filter (see `MetricsFilterState.havingOperator`'s
	 * remarks) - both null together turns the filter off. Same shape as setGroupByAttribute/
	 * setTopN - no name-list reload, which metrics exist doesn't depend on this filter, only
	 * the chart's own query does.
	 */
	setHaving(operator: MetricHavingOperator | null, value: number | null): void {
		this.#flushPendingSwitch();
		this.filter.havingOperator = operator;
		this.filter.havingValue = value;
		void this.runQuery();
	}

	/**
	 * Replaces the whole post-processing chain (see `MetricsFilterState.postProcessFunctions`'s
	 * remarks) - same shape as `setHaving`/`setGroupByAttribute`, no name-list reload.
	 */
	setPostProcessFunctions(functions: MetricPostProcessFunction[]): void {
		this.#flushPendingSwitch();
		this.filter.postProcessFunctions = functions;
		void this.runQuery();
	}

	setAutoRefreshEnabled(enabled: boolean): void {
		if (enabled === this.autoRefreshEnabled) return;
		this.autoRefreshEnabled = enabled;
		if (enabled) this.#startAutoRefresh();
		else this.#stopAutoRefresh();
	}

	#startAutoRefresh(): void {
		this.#stopAutoRefresh();
		this.#autoRefreshHandle = setInterval(() => this.#runActive(), AUTO_REFRESH_INTERVAL_MS);
	}

	/** Re-runs whichever mode's query is currently on-screen - every filter setter that affects both modes (time range, services, auto-refresh) goes through this instead of calling `runQuery`/`runFormulaQuery` directly, so it stays correct if `mode` flips without every one of those call sites needing its own branch. */
	#runActive(): void {
		if (this.mode === 'formula') void this.runFormulaQuery();
		else void this.runQuery();
	}

	// ---- Formula mode ----------------------------------------------------------

	/** Switches between single-metric and Formula mode, re-running whichever mode's query is now active - the other mode's fields (series/formulaSeries, etc.) are left exactly as they were, not cleared, so switching back doesn't lose anything already loaded. */
	setMode(mode: MetricsExplorerMode): void {
		if (mode === this.mode) return;
		this.mode = mode;
		this.#runActive();
	}

	/** Appends a new, empty query row with the next free letter - a no-op past `MAX_FORMULA_QUERIES` (the "+" control in `FormulaBuilder.svelte` disables itself at the same limit, this is just the defensive floor). Doesn't re-run the formula - an empty row has nothing to contribute until a metric is picked for it. */
	addFormulaQuery(): void {
		if (this.formulaQueries.length >= MAX_FORMULA_QUERIES) return;
		this.formulaQueries = [...this.formulaQueries, newFormulaQuery(nextFormulaLetter(this.formulaQueries))];
	}

	/** Removes one query row by letter - the row's letter is not reassigned to anything else (see `nextFormulaLetter`'s remarks), so an expression already referencing it just starts failing to parse... no, resolving it: `evaluateFormula` treats it as "referenced letter has no query defined" and surfaces that as `formulaWarning`/`formulaError`, same as any other missing reference, rather than a parse error - the expression text itself is still syntactically valid. */
	removeFormulaQuery(letter: string): void {
		this.#formulaAttributeKeysAborts.get(letter)?.abort();
		this.#formulaAttributeKeysAborts.delete(letter);
		this.formulaQueries = this.formulaQueries.filter((q) => q.letter !== letter);
		if (this.mode === 'formula') void this.runFormulaQuery();
	}

	/** Sets one query row's metric and re-fetches that row's own attribute keys (for its Group by picker) - independent of every other row's keys, unlike `loadKnownAttributeKeys`'s single, page-wide `knownAttributeKeys`. Resets the row's `groupByAttributeKey` (a key valid on the old metric may not exist on the new one - same "reset rather than carry over a possibly-invalid value" call `loadKnownAttributeKeys` makes for single mode). */
	setFormulaQueryMetric(letter: string, metric: MetricNameInfo | null): void {
		this.formulaQueries = this.formulaQueries.map((q) => (q.letter === letter ? { ...q, metric, groupByAttributeKey: null, attributeKeys: [] } : q));
		if (this.mode === 'formula') void this.runFormulaQuery();
		this.#loadFormulaAttributeKeys(letter, metric);
	}

	/** Fetches one row's attribute keys without touching `groupByAttributeKey` or re-running the formula - `setFormulaQueryMetric` (a real metric change, resets the row's group-by) and `applySavedViewState` (restoring a saved group-by verbatim) both need the fetch but want different side effects around it, so this is just the fetch. */
	#loadFormulaAttributeKeys(letter: string, metric: MetricNameInfo | null): void {
		this.#formulaAttributeKeysAborts.get(letter)?.abort();
		if (!metric) return;
		const abort = new AbortController();
		this.#formulaAttributeKeysAborts.set(letter, abort);
		this.formulaQueries = this.formulaQueries.map((q) => (q.letter === letter ? { ...q, attributeKeysLoading: true } : q));
		void (async () => {
			try {
				const range = this.#resolvedRange();
				const res = await getMetricAttributeKeys(
					{ metricName: metric.metricName, type: metric.type, filter: { from: range.from, to: range.to, services: [metric.serviceName] } },
					abort.signal
				);
				if (abort.signal.aborted) return;
				this.formulaQueries = this.formulaQueries.map((q) => (q.letter === letter ? { ...q, attributeKeys: res.keys, attributeKeysLoading: false } : q));
			} catch {
				if (abort.signal.aborted) return;
				// Non-critical, same as loadKnownAttributeKeys' own error handling - the row's Group by picker just shows no options until a retry (re-picking the metric).
				this.formulaQueries = this.formulaQueries.map((q) => (q.letter === letter ? { ...q, attributeKeysLoading: false } : q));
			}
		})();
	}

	setFormulaQueryGroupBy(letter: string, key: string | null): void {
		this.formulaQueries = this.formulaQueries.map((q) => (q.letter === letter ? { ...q, groupByAttributeKey: key } : q));
		if (this.mode === 'formula') void this.runFormulaQuery();
	}

	/** Parses on every keystroke (`FormulaBuilder.svelte`'s input is uncontrolled-debounced upstream of this, same as any other free-text filter field in this codebase) and only re-runs the query once it parses - a syntactically invalid formula has nothing to fetch/join, so `formulaExpressionError` alone carries the failure, `formulaError`/`formulaSeries` are left as whatever they last were. */
	setFormulaExpression(expression: string): void {
		this.formulaExpression = expression;
		const parsed = parseFormula(expression);
		this.formulaExpressionError = parsed.ok ? null : parsed.error;
		if (parsed.ok && this.mode === 'formula') void this.runFormulaQuery();
	}

	/**
	 * Fetches every query row referenced by the current (already-parsed) formula in
	 * parallel via the same `queryMetric` single-series endpoint `runQuery` uses - no
	 * dedicated formula endpoint (see ADR-0036: this is app-side post-processing over
	 * already-fetched rows, the same shape `HistogramQuantileEstimator` already uses
	 * server-side), then joins/evaluates via `evaluateFormula`. No TopN/Having override
	 * per row (v1 scope cut) - every row gets the server's own default cap
	 * (`MetricSeriesQueryBuilder.DefaultTopN` = 20), which is also why `evaluateFormula`'s
	 * own defensive `MAX_OUTPUT_SERIES` cap is never expected to bind (an inner join can't
	 * exceed its smallest input). No compare-period fetch either - see roadmap/ADR-0036 for
	 * why that's a deliberately separate follow-up, not folded in here.
	 */
	async runFormulaQuery(): Promise<void> {
		this.#formulaQueryAbort?.abort();

		const parsed = parseFormula(this.formulaExpression);
		if (!parsed.ok) {
			// setFormulaExpression already set formulaExpressionError for this same failure -
			// nothing new to surface, just nothing to fetch.
			return;
		}

		const abort = new AbortController();
		this.#formulaQueryAbort = abort;

		this.formulaLoading = true;
		this.formulaError = null;
		try {
			const range = this.#resolvedRange();
			const bucketWidthSeconds = pickBucketWidthSeconds(rangeSeconds(range));

			const refs = [...collectRefs(parsed.node)];
			const rows = refs.map((letter) => this.formulaQueries.find((q) => q.letter === letter)).filter((q): q is FormulaQueryDef => q != null);
			const missingMetric = rows.length < refs.length || rows.some((q) => !q.metric);
			if (missingMetric) {
				this.formulaSeries = [];
				this.formulaWarning = null;
				this.formulaError = 'Every query referenced by the formula needs a metric selected.';
				return;
			}

			const results = await Promise.all(
				rows.map((row) =>
					queryMetric(
						{
							metricName: row.metric!.metricName,
							type: row.metric!.type,
							bucketWidthSeconds,
							filter: { from: range.from, to: range.to, services: [row.metric!.serviceName] },
							groupByAttributeKey: row.groupByAttributeKey ?? undefined
						},
						abort.signal
					)
				)
			);
			if (abort.signal.aborted) return;

			const inputs = rows.map((row, i) => ({ letter: row.letter, series: results[i].series }));
			const evaluated = evaluateFormula(parsed.node, inputs);
			this.formulaSeries = evaluated.series;
			this.formulaWarning = evaluated.warning;
			this.formulaIntervalSeconds = bucketWidthSeconds;
			this.formulaRangeFrom = range.from;
			this.formulaRangeTo = range.to;
		} catch (err) {
			if (abort.signal.aborted) return;
			this.formulaError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.formulaLoading = false;
		}
	}

	#stopAutoRefresh(): void {
		if (this.#autoRefreshHandle !== null) {
			clearInterval(this.#autoRefreshHandle);
			this.#autoRefreshHandle = null;
		}
	}

	/** Serializes the current filter + selected metric into a saved view's opaque `state` payload - see `MetricsSavedViewState`. */
	toSavedViewState(): MetricsSavedViewState {
		return {
			timeRangePreset: this.filter.timeRangePreset,
			// Same ISO-string round-trip LogsExplorerState.toSavedViewState uses for its own
			// customRange - Date objects don't survive the saved-view payload's JSON encoding.
			customRange: this.filter.customRange
				? { from: this.filter.customRange.from.toISOString(), to: this.filter.customRange.to.toISOString() }
				: null,
			services: [...this.filter.services],
			compareEnabled: this.filter.compareEnabled,
			groupByAttributeKey: this.filter.groupByAttributeKey,
			topN: this.filter.topN,
			havingOperator: this.filter.havingOperator,
			havingValue: this.filter.havingValue,
			postProcessFunctions: this.filter.postProcessFunctions.map((f) => ({ ...f })),
			selectedMetric: this.selected
				? { metricName: this.selected.metricName, serviceName: this.selected.serviceName, type: this.selected.type }
				: null,
			mode: this.mode,
			formulaExpression: this.formulaExpression,
			formulaQueries: this.formulaQueries.map((q) => ({
				letter: q.letter,
				metric: q.metric ? { metricName: q.metric.metricName, serviceName: q.metric.serviceName, type: q.metric.type } : null,
				groupByAttributeKey: q.groupByAttributeKey
			}))
		};
	}

	/**
	 * Restores a saved view's filter + selected metric (defensively narrowed - see
	 * `LogsExplorerState.applySavedViewState`'s identical caveat). Reloads the name list
	 * for the restored filter first (which auto-selects some metric so the chart isn't
	 * blank - see `loadNames`), then re-selects the saved view's specific metric if it's
	 * still present among the results; falls back to whatever `loadNames` already picked
	 * if the saved metric no longer exists (e.g. that service stopped emitting it).
	 */
	async applySavedViewState(state: unknown): Promise<void> {
		this.#flushPendingSwitch();
		const s = (state ?? {}) as Partial<MetricsSavedViewState>;
		this.filter = {
			timeRangePreset: s.timeRangePreset ?? '1h',
			customRange: s.customRange ? { from: new Date(s.customRange.from), to: new Date(s.customRange.to) } : null,
			services: s.services ?? [],
			compareEnabled: s.compareEnabled ?? false,
			groupByAttributeKey: s.groupByAttributeKey ?? null,
			topN: s.topN ?? DEFAULT_TOP_N,
			havingOperator: s.havingOperator ?? null,
			havingValue: s.havingValue ?? null,
			// Absent from older saved views (pre-dates ADR-0038) - defaults to no
			// post-processing, the only state that existed then.
			postProcessFunctions: s.postProcessFunctions ?? []
		};
		await this.loadNames();
		const saved = s.selectedMetric;
		if (saved) {
			const match = this.names.find((m) => m.metricName === saved.metricName && m.serviceName === saved.serviceName);
			if (match) this.selectMetric(match);
		}

		// Older saved views (pre-Formula mode) carry none of the fields below - defaulted to
		// 'single' + two empty rows, the same state a fresh page load starts in.
		this.mode = s.mode ?? 'single';
		this.formulaExpression = s.formulaExpression ?? 'A / B';
		const parsedFormula = parseFormula(this.formulaExpression);
		this.formulaExpressionError = parsedFormula.ok ? null : parsedFormula.error;
		this.formulaQueries = (s.formulaQueries ?? [newFormulaQuery('A'), newFormulaQuery('B')]).map((q) => {
			// Re-resolved against the freshly-loaded `names` (not round-tripped as a full
			// MetricNameInfo - see MetricsSavedViewState.formulaQueries' own remarks); falls
			// back to no metric selected if it's gone, same soft-fail as selectedMetric above.
			const match = q.metric ? this.names.find((m) => m.metricName === q.metric!.metricName && m.serviceName === q.metric!.serviceName) : null;
			return { letter: q.letter, metric: match ?? null, groupByAttributeKey: q.groupByAttributeKey, attributeKeys: [], attributeKeysLoading: false };
		});
		// Each row's own attribute-key fetch (for its Group by picker), without disturbing
		// the groupByAttributeKey just restored above - see #loadFormulaAttributeKeys' remarks.
		for (const row of this.formulaQueries) {
			this.#loadFormulaAttributeKeys(row.letter, row.metric);
		}
		if (this.mode === 'formula') void this.runFormulaQuery();
	}

	dispose(): void {
		this.#namesAbort?.abort();
		this.#queryAbort?.abort();
		this.#attributeKeysAbort?.abort();
		this.#formulaQueryAbort?.abort();
		for (const abort of this.#formulaAttributeKeysAborts.values()) abort.abort();
		if (this.#pendingSwitchTimeout) clearTimeout(this.#pendingSwitchTimeout);
		this.#stopAutoRefresh();
	}
}
