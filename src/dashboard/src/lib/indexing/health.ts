// Parts-count health verdict for the Tables table's Parts column.
//
// Feedback: a bare "Parts: 6" (or "Parts: 146") tells a reader nothing without already
// knowing ClickHouse internals - what's a lot? MergeTree tables write each insert as its
// own immutable part and merge them down in the background; a table with far more active
// parts than its insert pattern should produce means merges aren't keeping up, which both
// slows queries (every part is a separate read) and heads toward ClickHouse refusing
// further inserts outright. That refusal has a real, non-arbitrary number attached -
// HIGH_FRAGMENTATION_PARTS below - so "high fragmentation" isn't a made-up style choice,
// it's "this table is approaching ClickHouse's own insert-throttling point."

export type PartsHealthTone = 'good' | 'warning';

export interface PartsHealth {
	tone: PartsHealthTone;
	label: string;
}

// ClickHouse's own MergeTree default for `parts_to_delay_insert` - the active-part count
// at which ClickHouse itself starts throttling inserts into a table to let background
// merges catch up. Flagging at the same number ClickHouse acts on (rather than a
// vibes-based threshold) is what makes the warning actionable instead of noise.
export const HIGH_FRAGMENTATION_PARTS = 150;

export function computePartsHealth(activeParts: number): PartsHealth {
	if (activeParts >= HIGH_FRAGMENTATION_PARTS) {
		return { tone: 'warning', label: 'High fragmentation' };
	}
	return { tone: 'good', label: 'Healthy' };
}

// Shared by the "Query performance" summary card and the "Query optimization" section's
// Search latency card - both color a millisecond value the same way, so the thresholds
// live in one place rather than two copies drifting apart.
export function latencyClass(ms: number | null | undefined): string {
	if (ms === null || ms === undefined) return '';
	if (ms >= 1000) return 'text-destructive';
	if (ms >= 300) return 'text-warning';
	return '';
}
