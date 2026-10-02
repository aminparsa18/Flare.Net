// "Download CSV" for time-series charts (MetricChart, FormulaChart, VolumeChart): writes the
// series already loaded for the chart, nothing refetched. One row per bucket, a UTC ISO
// timestamp, the same instant in the display zone, then one column per series labeled like
// the legend. Values are raw (unscaled, no unit suffix); a bucket a series has no point in is
// an empty cell.
import { displayTimeZone } from '$lib/time/display-zone.svelte';
import { formatDateTime } from '$lib/time/format';
import { downloadBlob } from '$lib/logs/export';
import { slugify } from './state.svelte';
import { toCsv } from './visualization';

export interface CsvSeries {
	label: string;
	points: readonly { time: number; value: number }[];
}

export function timeSeriesCsv(series: readonly CsvSeries[]): string {
	const times = [...new Set(series.flatMap((s) => s.points.map((p) => p.time)))].sort((a, b) => a - b);
	const byLabel = series.map((s) => new Map(s.points.map((p) => [p.time, p.value])));
	const header = ['timestamp_utc', `timestamp_${displayTimeZone.zone}`, ...series.map((s) => s.label)];
	const rows = times.map((t) => [new Date(t).toISOString(), formatDateTime(t), ...byLabel.map((v) => v.get(t) ?? null)]);
	return toCsv(header, rows);
}

export function downloadTimeSeriesCsv(series: readonly CsvSeries[], name: string): void {
	const blob = new Blob([timeSeriesCsv(series)], { type: 'text/csv;charset=utf-8' });
	downloadBlob(blob, `${slugify(name) || 'chart'}.csv`);
}
