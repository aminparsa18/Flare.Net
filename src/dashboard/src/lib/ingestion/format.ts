// Small formatting helpers shared by the Ingestion page's tiles/table/chart - no existing
// byte formatter anywhere else in the dashboard to reuse (every other page counts events,
// never bytes).

const compactNumber = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });

export function formatCount(n: number): string {
	return compactNumber.format(n);
}

const BYTE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

export function formatBytes(n: number): string {
	if (n <= 0) return '0 B';
	const exponent = Math.min(BYTE_UNITS.length - 1, Math.floor(Math.log(n) / Math.log(1024)));
	const value = n / 1024 ** exponent;
	// Every scaled unit already rounds (toFixed) - bytes themselves didn't, which was
	// invisible as long as every caller only ever passed whole byte counts, but a rate
	// (bytes/sec, see $lib/resources/format.ts's formatByteRate) is a division result and
	// can land under 1024 with a long fractional tail ("349.68019997311455 B/s") without
	// this.
	return `${exponent === 0 ? Math.round(value) : value.toFixed(value < 10 ? 1 : 0)} ${BYTE_UNITS[exponent]}`;
}

// Added for v10's pipeline-health section - "how long ago" for a last-flush timestamp or
// an oldest-pending-entry age, both of which arrive as seconds (or an ISO timestamp
// diffed against now), not a raw count like formatCount/formatBytes above.
export function formatAge(seconds: number | null | undefined): string {
	if (seconds === null || seconds === undefined) return '—';
	if (seconds < 1) return 'just now';
	if (seconds < 60) return `${Math.floor(seconds)}s ago`;
	if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
	return `${Math.floor(seconds / 3600)}h ago`;
}

export function secondsSince(iso: string | null, now: Date = new Date()): number | null {
	if (!iso) return null;
	return Math.max(0, (now.getTime() - new Date(iso).getTime()) / 1000);
}

