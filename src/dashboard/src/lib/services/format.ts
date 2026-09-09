// Request-rate formatting for the Traces page's Services tab - split out from a bare `.toFixed(1)`
// because a real-but-tiny rate (e.g. 0.02/s, one request every ~50s - a common shape
// for a demo/low-traffic service) rounds straight to "0.0/s", which reads as "no
// traffic" even though the service tile above already says otherwise. Same "<0.1%"
// escape hatch `$lib/indexing/format.ts`'s formatPercent already uses for a small-value
// rounding-to-zero problem, applied to a rate instead of a percentage.
export function formatRequestRate(perSecond: number): string {
	if (perSecond <= 0) return '0.0';
	if (perSecond < 0.1) return '<0.1';
	return perSecond.toFixed(1);
}
