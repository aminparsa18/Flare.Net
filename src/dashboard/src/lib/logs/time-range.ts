// Time-range presets for the toolbar's TimeRangePicker. Presets are resolved to concrete
// from/to instants at the moment they're used (not stored resolved) so "Last hour" stays
// "last hour" across repeated searches rather than freezing at first selection.

export type TimeRangePreset = '15m' | '1h' | '6h' | '24h' | '7d' | 'custom';

export interface TimeRangePresetOption {
	value: TimeRangePreset;
	label: string;
	durationMs: number | null; // null for 'custom' - resolved from an explicit range instead
}

export const TIME_RANGE_PRESETS: TimeRangePresetOption[] = [
	{ value: '15m', label: 'Last 15 minutes', durationMs: 15 * 60_000 },
	{ value: '1h', label: 'Last hour', durationMs: 60 * 60_000 },
	{ value: '6h', label: 'Last 6 hours', durationMs: 6 * 60 * 60_000 },
	{ value: '24h', label: 'Last 24 hours', durationMs: 24 * 60 * 60_000 },
	{ value: '7d', label: 'Last 7 days', durationMs: 7 * 24 * 60 * 60_000 },
	{ value: 'custom', label: 'Custom range', durationMs: null }
];

export interface ResolvedTimeRange {
	from: string; // ISO 8601, goes straight into LogFilter.from
	to: string; // ISO 8601, goes straight into LogFilter.to
}

/** `custom` is required (and used as-is) when `preset === 'custom'`; ignored otherwise. */
export function resolveTimeRange(
	preset: TimeRangePreset,
	custom?: { from: Date; to: Date }
): ResolvedTimeRange | null {
	if (preset === 'custom') {
		return custom ? { from: custom.from.toISOString(), to: custom.to.toISOString() } : null;
	}
	const option = TIME_RANGE_PRESETS.find((p) => p.value === preset);
	if (!option || option.durationMs == null) return null;
	const to = new Date();
	const from = new Date(to.getTime() - option.durationMs);
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
 * "Last 24 hours" -> "previous 24 hours" - every preset label follows the same "Last ..."
 * shape (see TIME_RANGE_PRESETS above), so this is a simple, always-correct substitution
 * rather than a second hand-written label table to keep in sync with the first.
 */
export function previousPeriodLabel(preset: TimeRangePreset): string {
	const label = TIME_RANGE_PRESETS.find((p) => p.value === preset)?.label ?? 'previous period';
	return label.replace(/^Last /, 'previous ');
}
