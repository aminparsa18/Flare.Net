// Reuses the exact byte/count formatting rules `$lib/ingestion/format.ts` already
// established - re-exported rather than duplicated, since both pages want identical
// "254 B" / "1.2 MB" / "3.4K" formatting and there's no reason for them to drift.

export { formatBytes, formatCount } from '$lib/ingestion/format';

export function formatRatio(compressed: number, uncompressed: number): string {
	if (compressed <= 0) return '—';
	return `${(uncompressed / compressed).toFixed(1)}x`;
}

// Query performance card wants whole milliseconds below 1s (sub-ms precision isn't
// actionable there) but a decimal once we're into seconds, where a tenth of a second is
// the difference someone cares about.
export function formatMs(ms: number): string {
	if (ms < 1000) return `${Math.round(ms)} ms`;
	return `${(ms / 1000).toFixed(1)} s`;
}

// Disk-usage card: a self-hosted deployment sits at a tiny fraction of its disk for a long
// time (the "11 MB / 100 GB" case from the redesign feedback), where rounding to a whole
// percent would just show "0%" and defeat the point of the stat - so sub-1% gets its own
// "<0.1%" rather than lying at "0%".
export function formatPercent(n: number): string {
	if (n <= 0) return '0%';
	if (n < 0.1) return '<0.1%';
	return `${n < 10 ? n.toFixed(1) : Math.round(n)}%`;
}
