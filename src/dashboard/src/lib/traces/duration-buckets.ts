// Duration buckets for the Traces facet sidebar. Mirrors
// `SpanAttributeValuesQueryBuilder.DurationBucketLowerBoundsNano` (Flare.Api) - the
// server returns each bucket's lower bound as the facet value, and this turns it back
// into the SpanFilter min/max range. Keep the two lists identical.

export const DURATION_BUCKET_LOWER_BOUNDS_NANO = [0, 1_000_000, 10_000_000, 100_000_000, 500_000_000, 1_000_000_000, 5_000_000_000] as const;

/** Inclusive min/max for the bucket starting at `lowerBoundNano` - max is `undefined` for the open-ended last bucket. */
export function durationBucketRange(lowerBoundNano: number): { min: number; max: number | undefined } {
	const index = DURATION_BUCKET_LOWER_BOUNDS_NANO.indexOf(lowerBoundNano as (typeof DURATION_BUCKET_LOWER_BOUNDS_NANO)[number]);
	const next = index >= 0 ? DURATION_BUCKET_LOWER_BOUNDS_NANO[index + 1] : undefined;
	return { min: lowerBoundNano, max: next === undefined ? undefined : next - 1 };
}

/** Bucket edges are all round ms/s values, so this skips `formatDurationNano`'s fixed decimals ("1.00ms"). */
function formatEdge(nano: number): string {
	return nano >= 1_000_000_000 ? `${nano / 1_000_000_000}s` : `${nano / 1_000_000}ms`;
}

export function durationBucketLabel(lowerBoundNano: number): string {
	const { max } = durationBucketRange(lowerBoundNano);
	if (max === undefined) return `≥ ${formatEdge(lowerBoundNano)}`;
	if (lowerBoundNano === 0) return `< ${formatEdge(max + 1)}`;
	return `${formatEdge(lowerBoundNano)} – ${formatEdge(max + 1)}`;
}
