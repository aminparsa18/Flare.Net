// "Down from at least N of M locations" as a threshold on `synthetic.up` with the Last aggregation
// (docs-internal/adr/0132-synthetic-location-quorum.md): Last averages each location's latest result, so the
// value is the fraction of locations that see the monitor up, and "N or more down" is "up fraction below
// (M - N + 0.5) / M" - halfway between the fractions for N and N - 1 down.

/** The `LessThan` threshold for "at least `minDown` of `total` locations are down". */
export function quorumThreshold(minDown: number, total: number): number {
	return Number((((total - minDown + 0.5) / total)).toFixed(4));
}

/** The inverse, for showing an existing rule: how many locations must be down for `threshold` to fire. */
export function quorumMinDown(threshold: number, total: number): number {
	return Math.min(total, Math.max(1, Math.round(total + 0.5 - threshold * total)));
}
