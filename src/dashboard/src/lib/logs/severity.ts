// Single source of truth for OTel SeverityNumber (0-24, db/clickhouse/0001_logs.sql)
// bucketing - both the toolbar's severity multi-select and the table/detail badges read
// from this, so "what counts as a warning" is defined in exactly one place.
//
// Standard OTel bucket boundaries: TRACE 1-4, DEBUG 5-8, INFO 9-12, WARN 13-16,
// ERROR 17-20, FATAL 21-24. SeverityNumber 0 is "unspecified" and isn't emitted by any
// real exporter, but is handled defensively (falls into the Trace bucket's badge
// treatment) rather than throwing.

import type { BadgeVariant } from '$lib/components/ui/badge';
import * as m from '$lib/paraglide/messages';

export type SeverityBucketId = 'trace' | 'debug' | 'info' | 'warn' | 'error' | 'fatal' | 'unspecified';

export interface SeverityBucket {
	/** Stable, English, lowercase - the internal identity (multi-select `value`, terminal
	 *  `-l/--level` argument matching). Never shown to a user - see {@link severityBucketLabel}
	 *  for that. Kept separate from the display label so switching locale can't change what
	 *  a saved filter or CLI command means, same "raw value vs. translated label" split
	 *  Phase 5's `roleLabel()` helpers use for the UserRole enum. */
	id: SeverityBucketId;
	min: number;
	max: number;
	variant: BadgeVariant;
}

export const SEVERITY_BUCKETS: SeverityBucket[] = [
	{ id: 'trace', min: 1, max: 4, variant: 'outline' },
	{ id: 'debug', min: 5, max: 8, variant: 'outline' },
	{ id: 'info', min: 9, max: 12, variant: 'secondary' },
	{ id: 'warn', min: 13, max: 16, variant: 'warning' },
	{ id: 'error', min: 17, max: 20, variant: 'destructive' },
	{ id: 'fatal', min: 21, max: 24, variant: 'destructive' }
];

const FALLBACK_BUCKET: SeverityBucket = { id: 'unspecified', min: 0, max: 0, variant: 'outline' };

/** Translated display text for a bucket - a function, not a field baked into
 *  SEVERITY_BUCKETS, so it re-evaluates against the current locale on every call (same
 *  reasoning `traces/status.ts`'s `statusLabel()`/`kindLabel()` are functions rather than
 *  a static labelled array). */
export function severityBucketLabel(bucket: SeverityBucket): string {
	switch (bucket.id) {
		case 'trace':
			return m.severityBucket_trace();
		case 'debug':
			return m.severityBucket_debug();
		case 'info':
			return m.severityBucket_info();
		case 'warn':
			return m.severityBucket_warn();
		case 'error':
			return m.severityBucket_error();
		case 'fatal':
			return m.severityBucket_fatal();
		default:
			return m.severityBucket_unspecified();
	}
}

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
