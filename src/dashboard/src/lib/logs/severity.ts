// Single source of truth for OTel SeverityNumber (0-24, db/clickhouse/0001_logs.sql)
// bucketing - both the toolbar's severity multi-select and the table/detail badges read
// from this, so "what counts as a warning" is defined in exactly one place.
//
// Standard OTel bucket boundaries: TRACE 1-4, DEBUG 5-8, INFO 9-12, WARN 13-16,
// ERROR 17-20, FATAL 21-24. SeverityNumber 0 is "unspecified" and isn't emitted by any
// real exporter, but is handled defensively (falls into the Trace bucket's badge
// treatment) rather than throwing.

import type { BadgeVariant } from '$lib/components/ui/badge';

export interface SeverityBucket {
	label: string;
	min: number;
	max: number;
	variant: BadgeVariant;
}

export const SEVERITY_BUCKETS: SeverityBucket[] = [
	{ label: 'Trace', min: 1, max: 4, variant: 'outline' },
	{ label: 'Debug', min: 5, max: 8, variant: 'outline' },
	{ label: 'Info', min: 9, max: 12, variant: 'secondary' },
	{ label: 'Warn', min: 13, max: 16, variant: 'secondary' },
	{ label: 'Error', min: 17, max: 20, variant: 'destructive' },
	{ label: 'Fatal', min: 21, max: 24, variant: 'destructive' }
];

const FALLBACK_BUCKET: SeverityBucket = { label: 'Unspecified', min: 0, max: 0, variant: 'outline' };

export function severityBucketFor(severityNumber: number): SeverityBucket {
	return SEVERITY_BUCKETS.find((b) => severityNumber >= b.min && severityNumber <= b.max) ?? FALLBACK_BUCKET;
}

export function severityVariant(severityNumber: number): BadgeVariant {
	return severityBucketFor(severityNumber).variant;
}

/** Expands a bucket to the exact SeverityNumber list `LogFilter.severityNumbers` needs (exact-match only, no range operator on the wire). */
export function severityNumbersForBucket(bucket: SeverityBucket): number[] {
	const numbers: number[] = [];
	for (let n = bucket.min; n <= bucket.max; n++) numbers.push(n);
	return numbers;
}
