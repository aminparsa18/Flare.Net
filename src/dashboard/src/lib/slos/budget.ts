import type { SloSeriesPoint } from '$lib/slos-api';

export interface BudgetPoint {
	timeUnixMs: number;
	/** Fraction of the error budget unspent at this point, over the trailing SLO window: 1 = untouched, 0 = spent, negative = overspent. */
	remaining: number;
}

/**
 * The error budget remaining at the end of each hourly bucket, as the SLO window would have
 * read it then: events and bad events of the trailing `windowDays` days. A rolling window is
 * what the status endpoint reports for "now", so the chart's last point matches it. Buckets
 * with no events in the trailing window yield no point.
 */
export function budgetSeries(series: SloSeriesPoint[], targetPercent: number, windowDays: number): BudgetPoint[] {
	const allowed = 1 - targetPercent / 100;
	if (allowed <= 0) return [];
	const windowMs = windowDays * 86_400_000;
	const points: BudgetPoint[] = [];
	let total = 0;
	let bad = 0;
	let tail = 0;
	for (let i = 0; i < series.length; i++) {
		total += series[i].total;
		bad += series[i].bad;
		// Drop buckets that have aged out of the trailing window.
		while (tail < i && series[tail].timeUnixMs <= series[i].timeUnixMs - windowMs) {
			total -= series[tail].total;
			bad -= series[tail].bad;
			tail++;
		}
		if (total > 0) points.push({ timeUnixMs: series[i].timeUnixMs, remaining: 1 - bad / total / allowed });
	}
	return points;
}

/** "42%" style, one decimal below 10%. Negative values (overspent) keep their sign. */
export function formatBudget(remaining: number | null): string {
	if (remaining == null) return '-';
	const pct = remaining * 100;
	return `${Math.abs(pct) < 10 ? pct.toFixed(1) : Math.round(pct)}%`;
}
