// Time-range presets for the toolbar's TimeRangePicker. Presets are resolved to concrete
// from/to instants at the moment they're used (not stored resolved) so "Last hour" stays
// "last hour" across repeated searches rather than freezing at first selection.
//
// `label` is deliberately NOT stored on TIME_RANGE_PRESETS/LOG_ONLY_TIME_RANGE_PRESETS
// below - these are module-scope consts, evaluated once per server process (not once per
// request), so a static translated string baked in here would freeze at whatever locale
// happened to be active the moment this module was first imported and never update for a
// later request in a different locale. presetLabel()/previousPeriodLabel() call m.*()
// fresh on every invocation instead - always call those for display, never read `.label`
// directly off these arrays (see MetricsToolbar.svelte/TracesToolbar.svelte for the
// correct pattern).
import * as m from '$lib/paraglide/messages';

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
	{ value: '5m', durationMs: 5 * 60_000 },
	{ value: '15m', durationMs: 15 * 60_000 },
	{ value: '1h', durationMs: 60 * 60_000 },
	{ value: '6h', durationMs: 6 * 60 * 60_000 },
	{ value: '24h', durationMs: 24 * 60 * 60_000 },
	{ value: '7d', durationMs: 7 * 24 * 60 * 60_000 },
	{ value: '30d', durationMs: 30 * 24 * 60 * 60_000 },
	{ value: '90d', durationMs: 90 * 24 * 60 * 60_000 },
	{ value: '365d', durationMs: 365 * 24 * 60 * 60_000 },
	{ value: 'custom', durationMs: null }
];

/**
 * Calendar-relative presets only Logs' TimeRangePicker exposes (see its own render for
 * display order). Not fixed durations, so they're kept out of TIME_RANGE_PRESETS rather
 * than filtered out by every other consumer.
 */
export const LOG_ONLY_TIME_RANGE_PRESETS: TimeRangePresetOption[] = [
	{ value: 'all' },
	{ value: 'today' },
	{ value: 'thisWeek' }
];

const PRESET_LABELS: Record<TimeRangePreset, () => string> = {
	'5m': m.timeRange_last5m,
	'15m': m.timeRange_last15m,
	'1h': m.timeRange_last1h,
	'6h': m.timeRange_last6h,
	'24h': m.timeRange_last24h,
	'7d': m.timeRange_last7d,
	'30d': m.timeRange_last30d,
	'90d': m.timeRange_last90d,
	'365d': m.timeRange_last365d,
	custom: m.timeRange_custom,
	all: m.timeRange_all,
	today: m.timeRange_today,
	thisWeek: m.timeRange_thisWeek
};

/** Looks up a preset's translated label - called fresh every time (never cached), unlike
 *  a static `.label` field, so it always reflects the current request/viewer's locale. */
export function presetLabel(preset: TimeRangePreset): string {
	return PRESET_LABELS[preset]?.() ?? preset;
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

const PREVIOUS_PERIOD_LABELS: Partial<Record<TimeRangePreset, () => string>> = {
	'5m': m.timeRange_previous5m,
	'15m': m.timeRange_previous15m,
	'1h': m.timeRange_previous1h,
	'6h': m.timeRange_previous6h,
	'24h': m.timeRange_previous24h,
	'7d': m.timeRange_previous7d,
	'30d': m.timeRange_previous30d,
	'90d': m.timeRange_previous90d,
	'365d': m.timeRange_previous365d,
	// A drag-to-zoom on MetricChart (see MetricsExplorerState.setCustomRange) lands on
	// 'custom' with an arbitrary, non-nameable duration - "previous 47 minutes" isn't a
	// preset anyone picked, so this gets a generic label instead of a real duration one.
	// The comparison math itself (previousPeriod/buildComparisonLines) doesn't need a
	// named preset at all - it just mirrors whatever span [from, to) actually was.
	custom: m.timeRange_previousPeriod
};

/**
 * "Last 24 hours" -> "previous 24 hours". Used to be a regex substitution on the English
 * label ("Last " -> "previous ") - that only worked because English happens to prefix
 * every fixed-duration label with "Last ", a pattern other languages don't share, so each
 * "previous ..." variant now has its own translated message instead. Only ever called
 * with a preset from TIME_RANGE_PRESETS (Metrics never offers the calendar-relative
 * LOG_ONLY_TIME_RANGE_PRESETS), so there's no "Today" -> "previous Today" case to worry
 * about - the fallback below only matters if that assumption is ever violated.
 */
export function previousPeriodLabel(preset: TimeRangePreset): string {
	return PREVIOUS_PERIOD_LABELS[preset]?.() ?? presetLabel(preset);
}

/**
 * Full-precision "20 Aug 2026 14:00:00.000 to 21 Aug 2026 09:15:00.000"-style label for an
 * explicit custom range - shared by TimeRangePicker.svelte (Logs' calendar picker) and
 * MetricsToolbar.svelte (which never offers 'custom' as a pickable preset, but can land on
 * one via MetricChart's drag-to-zoom - see MetricsExplorerState.setCustomRange). Day/month/
 * year plus 24h time down to the millisecond, not the coarser "Aug 20 - Aug 21" this used to
 * be - see TimeRangePicker's original remarks (still applicable) on why a short pan/zoom
 * that doesn't cross midnight needs to visibly show *something* moved. Built from plain Date
 * getters (not toLocaleTimeString) so the separators/24h-ness are guaranteed regardless of
 * locale, same reasoning VolumeChart's own axis-label formatting keeps to toLocaleString
 * only where locale variance is actually fine.
 */
export function formatCustomRangeLabel(range: { from: Date; to: Date } | null): string {
	if (!range) return m.timeRange_custom();
	const pad = (n: number, len = 2) => n.toString().padStart(len, '0');
	const fmt = (d: Date) => {
		const day = pad(d.getDate());
		const month = d.toLocaleDateString(undefined, { month: 'short' });
		const year = d.getFullYear();
		const time = `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`;
		return `${day} ${month} ${year} ${time}`;
	};
	return m.timeRangePicker_customRangeFormat({ from: fmt(range.from), to: fmt(range.to) });
}