// The fixed categorical series palette shared by every metric chart (MetricChart,
// FormulaChart, and the dashboard panel visualizations) - --chart-1..5, the `dataviz`
// skill's validated palette (see layout.css's chart-1..5 comment). Never cycled past 5: a
// chart with more series caps or folds them rather than inventing a 6th hue.

export const SERIES_COLOR_VARS = ['--chart-1', '--chart-2', '--chart-3', '--chart-4', '--chart-5'] as const;

/**
 * Deterministic per-series color: hashes the series' full identity (its label) into a slot
 * in the palette above, so the same series keeps its color across reloads, panels and
 * visualizations regardless of backend ordering. djb2 - collisions are cosmetic (two series
 * can legitimately share a hue). Prior art: SigNoz's per-label color hashing (signoz#4478).
 */
export function seriesColor(identity: string): string {
	let hash = 5381;
	for (let i = 0; i < identity.length; i++) {
		hash = (hash * 33) ^ identity.charCodeAt(i);
	}
	const index = Math.abs(hash) % SERIES_COLOR_VARS.length;
	return `var(${SERIES_COLOR_VARS[index]})`;
}

/** Palette slot by position (0-based) - for charts where rank, not identity, picks the color (pie slices, largest first). */
export function rankColor(index: number): string {
	return `var(${SERIES_COLOR_VARS[index % SERIES_COLOR_VARS.length]})`;
}
