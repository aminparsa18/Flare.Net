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
	return `${exponent === 0 ? value : value.toFixed(value < 10 ? 1 : 0)} ${BYTE_UNITS[exponent]}`;
}

