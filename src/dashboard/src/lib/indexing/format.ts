// Reuses the exact byte/count formatting rules `$lib/ingestion/format.ts` already
// established - re-exported rather than duplicated, since both pages want identical
// "254 B" / "1.2 MB" / "3.4K" formatting and there's no reason for them to drift.

export { formatBytes, formatCount } from '$lib/ingestion/format';

export function formatRatio(compressed: number, uncompressed: number): string {
	if (compressed <= 0) return '—';
	return `${(uncompressed / compressed).toFixed(1)}x`;
}
