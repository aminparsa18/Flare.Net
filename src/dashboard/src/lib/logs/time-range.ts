// Time-range presets for the toolbar's TimeRangePicker. Presets are resolved to concrete
// from/to instants at the moment they're used (not stored resolved) so "Last hour" stays
// "last hour" across repeated searches rather than freezing at first selection.

export type TimeRangePreset =
	| '5m'
	| '15m'
	| '1h'
	| '6h'
	| '24h'
	| '7d'
	| '30d'
	| '90d'
	| '365d'
	| 'all'
	| 'today'
	| 'thisWeek'
	| 'custom';

export interface TimeRangePresetOption {
	value: TimeRangePreset;
	label: string;
	/** Set only for fixed-duration entries - null for 'custom' and absent from LOG_ONLY_TIME_RANGE_PRESETS's calendar-relative entries. MetricChart's comparison-mode shift math reads this directly rather than re-deriving it from resolveTimeRange(). */
	durationMs?: number | null;
}

/**
 * Fixed-duration ("last N") presets plus 'custom' - the only kind that makes sense for
 * Metrics' and Traces' toolbars (see their own "only fixed-duration presets make sense
 * here" remarks): Metrics' comparison mode subtracts a fixed duration for "previous period"
 * (previousPeriod()), which a calendar-relative or open-ended range can't support. Logs'
 * own TimeRangePicker layers LOG_ONLY_TIME_RANGE_PRESETS on top of this list rather than
 * the other way around, so Metrics/Traces never have to filter anything new out as that
 * grows.
 */
export const TIME_RANGE_PRESETS: TimeRangePresetOption[] = [
	{ value: '5m', label: 'Last 5 minutes', durationMs: 5 * 60_000 },
	{ value: '15m', label: 'Last 15 minutes', durationMs: 15 * 60_000 },
	{ value: '1h', label: 'Last hour', durationMs: 60 * 60_000 },
	{ value: '6h', label: 'Last 6 hours', durationMs: 6 * 60 * 60_000 },
	{ value: '24h', label: 'Last 24 hours', durationMs: 24 * 60 * 60_000 },
	{ value: '7d', label: 'Last 7 days', durationMs: 7 * 24 * 60 * 60_000 },
	{ value: '30d', label: 'Last 30 days', durationMs: 30 * 24 * 60 * 60_000 },
	{ value: '90d', label: 'Last 90 days', durationMs: 90 * 24 * 60 * 60_000 },
	{ value: '365d', label: 'Last 365 days', durationMs: 365 * 24 * 60 * 60_000 },
	{ value: 'custom', label: 'Custom range', durationMs: null }
];

/**
 * Calendar-relative presets only Logs' TimeRangePicker exposes (see its own render for
 * display order). Not fixed durations, so they're kept out of TIME_RANGE_PRESETS rather
 * than filtered out by every other consumer.
 */
export const LOG_ONLY_TIME_RANGE_PRESETS: TimeRangePresetOption[] = [
	{ value: 'all', label: 'All time' },
	{ value: 'today', label: 'Today' },
	{ value: 'thisWeek', label: 'This week' }
];

/** Looks up a preset's label across both lists above - the one place that needs to know both exist. */
export function presetLabel(preset: TimeRangePreset): string {
	return (
		TIME_RANGE_PRESETS.find((p) => p.value === preset)?.label ??
		LOG_ONLY_TIME_RANGE_PRESETS.find((p) => p.value === preset)?.label ??
		preset
	);
}

export interface ResolvedTimeRange {
	from: string; // ISO 8601, goes straight into LogFilter.from
	to: string; // ISO 8601, goes straight into LogFilter.to
}

/**
 * `custom` is required (and used as-is) when `preset === 'custom'`; ignored otherwise.
 * Returns `null` only for an unresolvable `custom` (no range picked yet) - NOT for 'all',
 * which resolves to a concrete epoch-to-now range instead. That's deliberate:
 * LogFilterSqlBuilder defaults a missing `From` to `now - 1h` server-side (see its own
 * remarks), so an omitted/null `from` means "last hour", not "no lower bound" - sending
 * `null` for 'all' would silently narrow "All time" to the last hour instead of expanding
 * it. An explicit epoch bound gets the actual "everything" behavior instead, at no real
 * query cost (ClickHouse still only returns buckets/rows that exist).
 */
export function resolveTimeRange(
	preset: TimeRangePreset,
	custom?: { from: Date; to: Date }
): ResolvedTimeRange | null {
	if (preset === 'custom') {
		return custom ? { from: custom.from.toISOString(), to: custom.to.toISOString() } : null;
	}
	if (preset === 'all') return { from: new Date(0).toISOString(), to: new Date().toISOString() };
	if (preset === 'today') {
		const to = new Date();
		const from = new Date(to);
		from.setHours(0, 0, 0, 0);
		return { from: from.toISOString(), to: to.toISOString() };
	}
	if (preset === 'thisWeek') {
		const to = new Date();
		const from = new Date(to);
		from.setHours(0, 0, 0, 0);
		// Monday-based week start (ISO 8601), not locale-dependent - getDay() is 0=Sun..6=Sat.
		const day = from.getDay();
		from.setDate(from.getDate() - (day === 0 ? 6 : day - 1));
		return { from: from.toISOString(), to: to.toISOString() };
	}
	const durationMs = TIME_RANGE_PRESETS.find((p) => p.value === preset)?.durationMs;
	if (!durationMs) return null;
	const to = new Date();
	const from = new Date(to.getTime() - durationMs);
	return { from: from.toISOString(), to: to.toISOString() };
}

export function rangeSeconds(range: ResolvedTimeRange): number {
	return (new Date(range.to).getTime() - new Date(range.from).getTime()) / 1000;
}

/**
 * The window immediately before `range`, same duration, back-to-back (no gap) - "previous
 * 24h" for a "last 24h" range is `[from - 24h, from)`. Used by Metrics' comparison mode
 * (see MetricsExplorerState.runQuery) to fetch a second series for the same metric.
 */
export function previousPeriod(range: ResolvedTimeRange): ResolvedTimeRange {
	const durationMs = new Date(range.to).getTime() - new Date(range.from).getTime();
	return {
		from: new Date(new Date(range.from).getTime() - durationMs).toISOString(),
		to: range.from
	};
}

/**
 * "Last 24 hours" -> "previous 24 hours" - every fixed-duration preset's label follows the
 * same "Last ..." shape (see TIME_RANGE_PRESETS above), so this is a simple, always-correct
 * substitution rather than a second hand-written label table to keep in sync with the
 * first. Only ever called with a preset from TIME_RANGE_PRESETS (Metrics never offers the
 * calendar-relative LOG_ONLY_TIME_RANGE_PRESETS), so there's no "Today" -> "previous Today"
 * case to worry about.
 */
export function previousPeriodLabel(preset: TimeRangePreset): string {
	return presetLabel(preset).replace(/^Last /, 'previous ');
}