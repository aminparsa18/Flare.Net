// Picks a "nice" aggregate bucket width (POST /api/logs/aggregate's bucketWidthSeconds)
// targeting roughly 50-100 buckets across the active time range, snapping up to the next
// rung on a fixed ladder rather than dividing evenly - avoids ugly boundaries like
// 47-second buckets that don't align with anything a human would read on an axis.

const NICE_WIDTHS_SECONDS = [1, 5, 10, 30, 60, 300, 900, 3600, 21600, 86400] as const;
const TARGET_BUCKET_COUNT = 75;

export function pickBucketWidthSeconds(totalRangeSeconds: number): number {
	if (totalRangeSeconds <= 0) return NICE_WIDTHS_SECONDS[0];
	const idealWidth = totalRangeSeconds / TARGET_BUCKET_COUNT;
	return NICE_WIDTHS_SECONDS.find((w) => w >= idealWidth) ?? NICE_WIDTHS_SECONDS[NICE_WIDTHS_SECONDS.length - 1];
}
