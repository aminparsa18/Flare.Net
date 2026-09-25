// Picks a "nice" aggregate bucket width (POST /api/logs/aggregate's bucketWidthSeconds)
// targeting roughly 50-100 buckets across the active time range, snapping up to the next
// rung on a fixed ladder rather than dividing evenly - avoids ugly boundaries like
// 47-second buckets that don't align with anything a human would read on an axis.

const NICE_WIDTHS_SECONDS = [1, 5, 10, 30, 60, 300, 900, 3600, 21600, 86400] as const;
const TARGET_BUCKET_COUNT = 75;

/**
 * The choices IntervalMenu offers for a user-chosen width - the same ladder the auto-pick
 * snaps to, so a manual interval always lands on a boundary the auto one could have
 * picked too. Also the whitelist `normalizeBucketWidthSeconds` checks an untrusted saved
 * view payload against.
 */
export const BUCKET_WIDTH_OPTIONS_SECONDS: readonly number[] = NICE_WIDTHS_SECONDS;

/**
 * Upper bound on buckets a user-chosen width may produce - high enough for the roadmap's
 * own "1m buckets over 24h" (1,440), low enough that an ungrouped chart stays well under
 * Flare.Api's default `Query__MaxResultRows` (10,000, truncating) and the SVG doesn't have
 * to draw tens of thousands of bars. A width that would exceed it for the current range
 * is raised, not rejected - see `resolveBucketWidthSeconds`.
 */
export const MAX_BUCKET_COUNT = 1500;

export function pickBucketWidthSeconds(totalRangeSeconds: number): number {
	if (totalRangeSeconds <= 0) return NICE_WIDTHS_SECONDS[0];
	const idealWidth = totalRangeSeconds / TARGET_BUCKET_COUNT;
	return NICE_WIDTHS_SECONDS.find((w) => w >= idealWidth) ?? NICE_WIDTHS_SECONDS[NICE_WIDTHS_SECONDS.length - 1];
}

/** Whether `widthSeconds` stays within `MAX_BUCKET_COUNT` over `totalRangeSeconds` - IntervalMenu disables the rungs that don't. */
export function isBucketWidthAllowed(totalRangeSeconds: number, widthSeconds: number): boolean {
	return totalRangeSeconds / widthSeconds <= MAX_BUCKET_COUNT;
}

/**
 * The width actually sent for a chart query: `override` null = auto (`pickBucketWidthSeconds`);
 * otherwise the user's choice, raised to the smallest ladder rung that fits
 * `MAX_BUCKET_COUNT` when the range has since grown past what it allows (e.g. a saved
 * 1m view reopened on a 7d range) rather than refusing to draw anything.
 */
export function resolveBucketWidthSeconds(totalRangeSeconds: number, override: number | null): number {
	if (override == null) return pickBucketWidthSeconds(totalRangeSeconds);
	if (isBucketWidthAllowed(totalRangeSeconds, override)) return override;
	return (
		NICE_WIDTHS_SECONDS.find((w) => w >= override && isBucketWidthAllowed(totalRangeSeconds, w)) ??
		NICE_WIDTHS_SECONDS[NICE_WIDTHS_SECONDS.length - 1]
	);
}

/** Narrows an untrusted (saved view) value to a ladder rung or null (auto). */
export function normalizeBucketWidthSeconds(value: unknown): number | null {
	return typeof value === 'number' && BUCKET_WIDTH_OPTIONS_SECONDS.includes(value) ? value : null;
}

/** A `pickBucketWidthSeconds` result as a short label ("1m", "6h", ...) - every rung on `NICE_WIDTHS_SECONDS` lands on a whole s/m/h/d value, so no decimals to worry about. */
export function formatBucketWidthSeconds(seconds: number): string {
	if (seconds < 60) return `${seconds}s`;
	if (seconds < 3600) return `${Math.round(seconds / 60)}m`;
	if (seconds < 86400) return `${Math.round(seconds / 3600)}h`;
	return `${Math.round(seconds / 86400)}d`;
}
