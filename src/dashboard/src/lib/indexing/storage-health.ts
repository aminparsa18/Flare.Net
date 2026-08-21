// "Storage health" section: turns the raw numbers already on this page into verdicts,
// the same "answer the actual question, don't just expose the ClickHouse fact" principle
// behind every other change to this page this session. Each check states its own
// threshold in its doc comment - a real, stated number to point to, not a vibes-based
// judgment call, same discipline as HIGH_FRAGMENTATION_PARTS/SLOW_QUERY_THRESHOLD_MS
// elsewhere on this page.
//
// Deliberately missing a "skip index usage" check (e.g. "all configured indexes are being
// used", or "idx_body only helped 3% of matching queries") - researched whether ClickHouse
// exposes that as reliable telemetry before building anything here, and it doesn't (see
// Planning.md's "Later" section, logged the same day this module was written). An
// `unavailable`-tone placeholder row names the gap explicitly instead of silently omitting
// it or, worse, inventing a number.

import type { IndexingStatsResponse, TableStorageInfo } from '../indexing-api';
import { formatBytes, formatCount, formatPercent, formatRatio } from './format';
import { computePartsHealth } from './health';
import { averageDailyGrowth, type GrowthBreakdown } from './growth';

export type StorageHealthTone = 'good' | 'warning' | 'unavailable';

export interface StorageHealthCheck {
	id: string;
	tone: StorageHealthTone;
	title: string;
	detail: string;
}

// Below this ratio is unusual for structured telemetry (logs/traces/metrics routinely
// compress 5-40x with ZSTD/LZ4) - worth a look rather than assumed fine, but loose enough
// that a normal deployment never trips it.
const LOW_COMPRESSION_RATIO = 2;

function checkCompression(tables: TableStorageInfo[]): StorageHealthCheck {
	const compressed = tables.reduce((sum, t) => sum + t.compressedBytes, 0);
	const uncompressed = tables.reduce((sum, t) => sum + t.uncompressedBytes, 0);
	const ratio = compressed > 0 ? uncompressed / compressed : 0;
	const detail = `${formatBytes(compressed)} compressed from ${formatBytes(uncompressed)} uncompressed - ${formatRatio(compressed, uncompressed)} compression ratio`;

	if (compressed <= 0) {
		return { id: 'compression', tone: 'good', title: 'Compression', detail: 'No data stored yet.' };
	}
	if (ratio < LOW_COMPRESSION_RATIO) {
		return {
			id: 'compression',
			tone: 'warning',
			title: 'Compression',
			detail: `${detail} - lower than typical for structured log/trace/metric data, may be worth a look.`
		};
	}
	return { id: 'compression', tone: 'good', title: 'Compression', detail };
}

function checkParts(tables: TableStorageInfo[]): StorageHealthCheck {
	const fragmented = tables.filter((t) => computePartsHealth(t.activeParts).tone === 'warning');
	if (fragmented.length === 0) {
		return { id: 'parts', tone: 'good', title: 'Parts', detail: 'All tables have a healthy number of active parts.' };
	}
	return {
		id: 'parts',
		tone: 'warning',
		title: 'Parts',
		detail: `${fragmented.length} table${fragmented.length === 1 ? '' : 's'} with a high number of active parts: ${fragmented
			.map((t) => t.tableName)
			.join(', ')}.`
	};
}

// A single table dominating row volume isn't inherently wrong (a metrics-heavy workload
// looks like this by design) - flagged as worth noting, not as a problem, past this share
// of total rows.
const HIGH_ROW_CONCENTRATION_PERCENT = 70;

function checkRowConcentration(tables: TableStorageInfo[]): StorageHealthCheck {
	const totalRows = tables.reduce((sum, t) => sum + t.rows, 0);
	if (totalRows <= 0) {
		return { id: 'row-concentration', tone: 'good', title: 'Row distribution', detail: 'No data stored yet.' };
	}

	const largest = tables.reduce((max, t) => (t.rows > max.rows ? t : max), tables[0]);
	const percent = (largest.rows / totalRows) * 100;
	if (percent >= HIGH_ROW_CONCENTRATION_PERCENT) {
		return {
			id: 'row-concentration',
			tone: 'warning',
			title: 'Row distribution',
			detail: `${largest.tableName} accounts for ${formatPercent(percent)} of stored rows (${formatCount(largest.rows)} of ${formatCount(totalRows)}).`
		};
	}
	return {
		id: 'row-concentration',
		tone: 'good',
		title: 'Row distribution',
		detail: `No single table dominates row volume (largest is ${largest.tableName} at ${formatPercent(percent)}).`
	};
}

// How many times faster the fastest-growing signal has to be growing than the slowest for
// it to be worth surfacing, rather than normal day-to-day variation between signals with
// very different ingestion shapes (one metric point vs. one log line vs. one span).
const GROWTH_SKEW_RATIO = 3;
const SIGNAL_BREAKDOWNS: { breakdown: GrowthBreakdown; label: string }[] = [
	{ breakdown: 'logs', label: 'Logs' },
	{ breakdown: 'traces', label: 'Traces' },
	{ breakdown: 'metrics', label: 'Metrics' }
];

function checkGrowthRateSkew(stats: IndexingStatsResponse): StorageHealthCheck {
	if (!stats.growthAvailable) {
		return {
			id: 'growth-skew',
			tone: 'unavailable',
			title: 'Signal growth',
			detail: 'Not available - system.part_log isn’t queryable on this ClickHouse deployment.'
		};
	}

	if (!stats.tables.some((t) => t.rows > 0)) {
		return { id: 'growth-skew', tone: 'good', title: 'Signal growth', detail: 'No data stored yet.' };
	}

	const rates = SIGNAL_BREAKDOWNS.map(({ breakdown, label }) => ({
		label,
		bytesPerDay: averageDailyGrowth(stats.growth, breakdown)?.bytesPerDay ?? 0
	}));

	const growing = rates.filter((r) => r.bytesPerDay > 0);
	if (growing.length < 2) {
		return { id: 'growth-skew', tone: 'good', title: 'Signal growth', detail: 'Not enough recent growth across signals to compare yet.' };
	}

	const fastest = growing.reduce((max, r) => (r.bytesPerDay > max.bytesPerDay ? r : max), growing[0]);
	const slowest = rates.reduce((min, r) => (r.bytesPerDay < min.bytesPerDay ? r : min), rates[0]);

	if (slowest.bytesPerDay <= 0) {
		return {
			id: 'growth-skew',
			tone: 'warning',
			title: 'Signal growth',
			detail: `${fastest.label} is growing (${formatBytes(fastest.bytesPerDay)}/day) while ${slowest.label} shows no growth in the past week.`
		};
	}

	const ratio = fastest.bytesPerDay / slowest.bytesPerDay;
	if (ratio >= GROWTH_SKEW_RATIO) {
		return {
			id: 'growth-skew',
			tone: 'warning',
			title: 'Signal growth',
			detail: `${fastest.label} is growing ${ratio.toFixed(1)}× faster than ${slowest.label} (${formatBytes(fastest.bytesPerDay)}/day vs. ${formatBytes(slowest.bytesPerDay)}/day).`
		};
	}
	return { id: 'growth-skew', tone: 'good', title: 'Signal growth', detail: 'Growth rates are broadly proportional across logs, traces, and metrics.' };
}

const SKIP_INDEX_USAGE_CHECK: StorageHealthCheck = {
	id: 'skip-index-usage',
	tone: 'unavailable',
	title: 'Skip index usage',
	detail:
		'Not available yet - ClickHouse doesn’t expose reliable per-index usage telemetry (system.query_log has no per-index attribution). Tracked as a research item in Planning.md.'
};

export function computeStorageHealth(stats: IndexingStatsResponse): StorageHealthCheck[] {
	return [checkCompression(stats.tables), checkParts(stats.tables), checkRowConcentration(stats.tables), checkGrowthRateSkew(stats), SKIP_INDEX_USAGE_CHECK];
}
