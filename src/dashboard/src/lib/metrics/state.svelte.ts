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

import { getMetricNames, queryMetric, type MetricNameInfo, type MetricPointType, type MetricSeries } from '$lib/metrics-api';
import { resolveTimeRange, rangeSeconds, previousPeriod, type TimeRangePreset, type ResolvedTimeRange } from '$lib/logs/time-range';
import { pickBucketWidthSeconds } from '$lib/logs/bucket-width';

export interface MetricsFilterState {
	timeRangePreset: TimeRangePreset;
	services: string[];
	/** "Compare with previous period" toggle - see MetricChart.svelte's comparison-mode remarks. Unlike Logs' patternId/attribute drill-downs, this *is* carried in a saved view (toSavedViewState/applySavedViewState below) - it's a display preference for the metric, not a one-off hop. */
	compareEnabled: boolean;
}

/**
 * A saved view's `state` payload for `pageType: 'Metrics'` - `MetricsFilterState` plus the
 * currently-charted metric's identity. Unlike Logs/Traces, "the view" on this page isn't
 * just the filter: which (metricName, serviceName) is selected is equally part of what a
 * saved view should reproduce, so it's carried alongside the filter here rather than
 * needing a second saved-view concept.
 */
export interface MetricsSavedViewState extends MetricsFilterState {
	selectedMetric: { metricName: string; serviceName: string; type: MetricPointType } | null;
}

/** Identifies one picker entry/selection - a (metricName, serviceName) pair, since the same metric name can be emitted by more than one service (see MetricNameInfo.serviceName's C# doc comment). */
function metricKey(metric: Pick<MetricNameInfo, 'metricName' | 'serviceName'>): string {
	return `${metric.metricName} ${metric.serviceName}`;
}

export class MetricsExplorerState {
	filter = $state<MetricsFilterState>({
		timeRangePreset: '1h',
		services: [],
		compareEnabled: false
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
	// The bucketWidthSeconds actually sent with the current `series` - for the chart
	// header's "1m interval" metadata row. Reset to null at the start of every query
	// (not just replaced alongside `series`) so a metric switch never briefly shows the
	// *previous* metric's interval next to a chart that hasn't caught up yet.
	intervalSeconds = $state<number | null>(null);

	// Not derived from `names` (which is already narrowed by the current service
	// filter - self-narrowing the picklist as soon as one service is chosen, same
	// chicken-and-egg problem TracesExplorerState.knownServices' own comment
	// documents). A separate, wide-window (7d), unfiltered-by-service discovery call
	// instead, same workaround.
	knownServices = $state.raw<string[]>([]);

	#namesAbort: AbortController | null = null;
	#queryAbort: AbortController | null = null;

	#resolvedRange(): ResolvedTimeRange {
		// Every preset MetricsToolbar actually offers ('custom' is filtered out, same as
		// TracesToolbar) resolves non-null - the '1h' fallback is a defensive default,
		// never expected to be hit.
		return resolveTimeRange(this.filter.timeRangePreset) ?? resolveTimeRange('1h')!;
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
				void this.runQuery();
			}
		} catch (err) {
			if (abort.signal.aborted) return;
			this.namesError = err instanceof Error ? err.message : String(err);
		} finally {
			if (!abort.signal.aborted) this.namesLoading = false;
		}
	}

	async runQuery(): Promise<void> {
		this.#queryAbort?.abort();

		if (!this.selected) {
			this.series = [];
			this.previousSeries = [];
			this.queryError = null;
			this.intervalSeconds = null;
			return;
		}

		const abort = new AbortController();
		this.#queryAbort = abort;
		const metric = this.selected;
		const compareEnabled = this.filter.compareEnabled;

		this.queryLoading = true;
		this.queryError = null;
		this.intervalSeconds = null;
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
					{ metricName: metric.metricName, type: metric.type, bucketWidthSeconds, filter: filterFor(range) },
					abort.signal
				),
				compareEnabled
					? queryMetric(
							{
								metricName: metric.metricName,
								type: metric.type,
								bucketWidthSeconds,
								filter: filterFor(previousPeriod(range))
							},
							abort.signal
						).catch(() => null)
					: Promise.resolve(null)
			]);
			if (abort.signal.aborted) return;
			this.series = current.series;
			this.previousSeries = previous?.series ?? [];
			this.intervalSeconds = bucketWidthSeconds;
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
		void this.runQuery();
	}

	setTimeRangePreset(preset: TimeRangePreset): void {
		this.filter.timeRangePreset = preset;
		void this.loadNames();
		void this.runQuery();
	}

	setServices(services: string[]): void {
		this.filter.services = services;
		void this.loadNames();
		void this.runQuery();
	}

	/** No name-list reload needed, unlike setTimeRangePreset/setServices - which metrics exist doesn't depend on compare mode, only the chart's own query does. */
	setCompareEnabled(enabled: boolean): void {
		this.filter.compareEnabled = enabled;
		void this.runQuery();
	}

	/** Serializes the current filter + selected metric into a saved view's opaque `state` payload - see `MetricsSavedViewState`. */
	toSavedViewState(): MetricsSavedViewState {
		return {
			timeRangePreset: this.filter.timeRangePreset,
			services: [...this.filter.services],
			compareEnabled: this.filter.compareEnabled,
			selectedMetric: this.selected
				? { metricName: this.selected.metricName, serviceName: this.selected.serviceName, type: this.selected.type }
				: null
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
		const s = (state ?? {}) as Partial<MetricsSavedViewState>;
		this.filter = { timeRangePreset: s.timeRangePreset ?? '1h', services: s.services ?? [], compareEnabled: s.compareEnabled ?? false };
		await this.loadNames();
		const saved = s.selectedMetric;
		if (saved) {
			const match = this.names.find((m) => m.metricName === saved.metricName && m.serviceName === saved.serviceName);
			if (match) this.selectMetric(match);
		}
	}

	dispose(): void {
		this.#namesAbort?.abort();
		this.#queryAbort?.abort();
	}
}
