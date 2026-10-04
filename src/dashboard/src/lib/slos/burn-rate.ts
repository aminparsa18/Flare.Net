// The standard multi-window burn-rate alert pairs (Google SRE workbook), scaled to an SLO's
// window: "page when 2% of the budget burns in 1h (confirmed over 5m); ticket when 5% burns in
// 6h (confirmed over 30m)". A burn rate of B sustained for `alertHours` spends B * alertHours /
// (windowDays * 24) of the budget, so the rate that spends a fraction F is F * windowDays * 24 /
// alertHours - 14.4 for a 30-day window's 2%-in-1h.

export interface BurnAlertPreset {
	key: 'fast' | 'slow';
	longWindowSeconds: number;
	shortWindowSeconds: number;
	/** Fraction of the whole error budget the long window's burn would spend. */
	budgetFraction: number;
	severity: 'Critical' | 'Warning';
}

export const BURN_ALERT_PRESETS: BurnAlertPreset[] = [
	{ key: 'fast', longWindowSeconds: 3600, shortWindowSeconds: 300, budgetFraction: 0.02, severity: 'Critical' },
	{ key: 'slow', longWindowSeconds: 21600, shortWindowSeconds: 1800, budgetFraction: 0.05, severity: 'Warning' }
];

/** The burn-rate threshold of `preset` for an SLO with a `windowDays`-day window, to one decimal. */
export function burnRateThreshold(preset: BurnAlertPreset, windowDays: number): number {
	const alertHours = preset.longWindowSeconds / 3600;
	const rate = (preset.budgetFraction * windowDays * 24) / alertHours;
	return Math.round(rate * 10) / 10;
}

/** "5m", "30m", "1h", "6h", "24h" - for a window length in seconds. */
export function formatWindow(seconds: number): string {
	if (seconds % 86400 === 0 && seconds >= 86400 * 2) return `${seconds / 86400}d`;
	if (seconds % 3600 === 0) return `${seconds / 3600}h`;
	return `${Math.round(seconds / 60)}m`;
}

/** "14.4" / "6" - at most one decimal, no trailing zero. */
export function formatBurnRate(rate: number): string {
	return String(Math.round(rate * 10) / 10);
}
