// Shared per-signal aggregation over IndexingStatsResponse.growth (system.part_log's
// per-table/per-day new-part bytes+rows). Three consumers now read growth the same way -
// the growth chart's breakdown toggle, the summary tiles' "Ingestion growth" card, and
// Storage health's cross-signal growth-rate check - so the "which tables count as which
// signal" and "average bytes/day over the trailing window" logic live here once instead
// of three slightly-drifting copies.

import type { StorageGrowthPoint } from '../indexing-api';

export type GrowthBreakdown = 'total' | 'logs' | 'traces' | 'metrics';

// spans is the traces table's physical name (see db/clickhouse/*.sql) - "metrics_" is a
// prefix match rather than three hardcoded names so a future metrics_* table folds in
// automatically instead of silently falling out of the "Metrics" breakdown.
export function matchesBreakdown(tableName: string, breakdown: GrowthBreakdown): boolean {
	switch (breakdown) {
		case 'total':
			return true;
		case 'logs':
			return tableName === 'logs';
		case 'traces':
			return tableName === 'spans';
		case 'metrics':
			return tableName.startsWith('metrics_');
	}
}

// One quiet or one unusually heavy day alone would be a noisy signal - averaging over a
// trailing week smooths that out while staying recent enough to reflect current behavior,
// not the whole 30-day history the growth chart plots.
export const GROWTH_WINDOW_DAYS = 7;

export interface AverageDailyGrowth {
	bytesPerDay: number;
	rowsPerDay: number;
	windowDays: number;
}

// Average daily bytes/rows added, over the trailing GROWTH_WINDOW_DAYS days present in
// `growth`, restricted to tables matching `breakdown`. Returns null when there's no growth
// data in the window at all (not the same as a genuine zero - callers tell those apart).
export function averageDailyGrowth(growth: StorageGrowthPoint[], breakdown: GrowthBreakdown = 'total'): AverageDailyGrowth | null {
	const days = [...new Set(growth.map((p) => p.day))].sort();
	const window = days.slice(-GROWTH_WINDOW_DAYS);
	if (window.length === 0) return null;

	const windowSet = new Set(window);
	const filtered = growth.filter((p) => windowSet.has(p.day) && matchesBreakdown(p.tableName, breakdown));
	const totalBytes = filtered.reduce((sum, p) => sum + p.bytes, 0);
	const totalRows = filtered.reduce((sum, p) => sum + p.rows, 0);
	return { bytesPerDay: totalBytes / window.length, rowsPerDay: totalRows / window.length, windowDays: window.length };
}
