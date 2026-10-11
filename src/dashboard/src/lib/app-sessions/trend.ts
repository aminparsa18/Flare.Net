// Turns the release-health trend points (ADR-0189) into per-version series for the sessions chart.

import type { ReleaseHealthTrendPoint } from '$lib/app-sessions-api';

export interface TrendSeries {
	version: string;
	sessions: number;
	/** One entry per bucket that had sessions; the rate is the percent that did not crash. */
	points: { t: number; rate: number; sessions: number; crashed: number }[];
}

/** The most versions drawn; more would be unreadable lines, and the table above still lists them all. */
export const MAX_TREND_SERIES = 6;

/** Busiest versions first (by total sessions), at most `limit` of them. */
export function buildTrendSeries(points: ReleaseHealthTrendPoint[], limit = MAX_TREND_SERIES): TrendSeries[] {
	const byVersion = new Map<string, TrendSeries>();
	for (const p of points) {
		if (p.sessions === 0) continue;
		let series = byVersion.get(p.version);
		if (!series) {
			series = { version: p.version, sessions: 0, points: [] };
			byVersion.set(p.version, series);
		}
		series.sessions += p.sessions;
		series.points.push({ t: p.bucketUnixMs, rate: (100 * (p.sessions - p.crashedSessions)) / p.sessions, sessions: p.sessions, crashed: p.crashedSessions });
	}
	return [...byVersion.values()]
		.sort((a, b) => b.sessions - a.sessions || a.version.localeCompare(b.version))
		.slice(0, limit)
		.map((s) => ({ ...s, points: s.points.sort((a, b) => a.t - b.t) }));
}

/** The y-axis floor: 99 while everything is healthy, otherwise the lowest rate drawn rounded down to a whole percent (to a ten below 90). */
export function trendAxisMin(series: TrendSeries[]): number {
	let lowest = 100;
	for (const s of series) for (const p of s.points) lowest = Math.min(lowest, p.rate);
	if (lowest >= 99) return 99;
	return lowest >= 90 ? Math.floor(lowest) : Math.max(0, Math.floor(lowest / 10) * 10);
}
