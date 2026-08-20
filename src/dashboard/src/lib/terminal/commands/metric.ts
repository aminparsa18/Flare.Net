// `metric` - mimics flare.cli's MetricCommand.cs (single-metric chart) by calling the same
// POST /api/metrics/names + POST /api/metrics/query the dashboard's own Metrics page uses
// (getMetricNames()/queryMetric() from $lib/metrics-api.ts), rather than any new backend
// surface - same "already-authenticated dashboard API, different front end" shape as
// commands/tail.ts/traces.ts/trace.ts. Renders ASCII sparklines instead of MetricChart's
// SVG lines, but - unlike MetricCommand.cs's own necessarily-simplified C# port - reuses
// this dashboard's real $lib/metrics/axis.ts and $lib/logs/bucket-width.ts helpers
// directly for unit formatting/bucket-width picking, so values read with the exact same
// ms<->s/B<->MB scaling the chart itself uses, not a "declared unit as-is" approximation.

import { getMetricNames, queryMetric, type MetricSeries, type MetricSeriesPoint } from '$lib/metrics-api';
import { pickBucketWidthSeconds, formatBucketWidthSeconds } from '$lib/logs/bucket-width';
import { resolveAxisScale, formatAtScale } from '$lib/metrics/axis';
import type { TerminalCommand, TerminalWriter } from '../types';
import { parseSince } from './metrics';

class UsageError extends Error {}

const SPARK_WIDTH = 60;
const MAX_SERIES = 5;
const SPARK_CHARS = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

interface ParsedArgs {
	metricName: string;
	service?: string;
	groupBy?: string;
	mode?: string;
	sinceText: string;
	sinceMs: number;
}

function parseArgs(args: string[]): ParsedArgs {
	const metricName = args[0];
	if (!metricName || metricName.startsWith('-')) {
		throw new UsageError('metric: a metric name is required - usage: metric <name> [options]');
	}

	const result: ParsedArgs = { metricName, sinceText: '1h', sinceMs: 60 * 60_000 };

	for (let i = 1; i < args.length; i++) {
		const arg = args[i];
		switch (arg) {
			case '-s':
			case '--service':
				result.service = requireValue(args, ++i, arg);
				break;
			case '--group-by':
				result.groupBy = requireValue(args, ++i, arg);
				break;
			case '--mode':
				result.mode = requireValue(args, ++i, arg);
				break;
			case '--since':
				result.sinceText = requireValue(args, ++i, arg);
				result.sinceMs = parseSince(result.sinceText);
				break;
			default:
				throw new UsageError(`metric: unrecognized option '${arg}'`);
		}
	}

	return result;
}

function requireValue(args: string[], index: number, flag: string): string {
	const value = args[index];
	if (value === undefined) throw new UsageError(`metric: ${flag} requires a value`);
	return value;
}

// Mirrors MetricChart.svelte's SumMode/HistogramMode defaults - see MetricCommand.cs's
// own TryResolveMode for the identical rules.
function resolveMode(type: string, requested: string | undefined): string {
	const normalized = requested?.trim().toLowerCase();
	switch (type) {
		case 'Gauge':
			if (normalized !== undefined) throw new UsageError("metric: Gauge metrics have no aggregation mode - --mode isn't valid here.");
			return 'value';
		case 'Sum': {
			const mode = normalized ?? 'rate';
			if (mode === 'sum' || mode === 'rate' || mode === 'count') return mode;
			throw new UsageError(`metric: unknown --mode '${requested}' for a Sum metric - expected one of: sum, rate, count.`);
		}
		case 'Histogram': {
			const mode = normalized ?? 'percentiles';
			if (['percentiles', 'mean', 'p75', 'p95', 'max'].includes(mode)) return mode;
			throw new UsageError(`metric: unknown --mode '${requested}' for a Histogram metric - expected one of: percentiles, mean, p75, p95, max.`);
		}
		default:
			return normalized ?? '';
	}
}

function modeLabel(type: string, mode: string): string {
	if (type === 'Sum') return mode === 'sum' ? 'Sum' : mode === 'count' ? 'Count' : 'Rate';
	if (type === 'Histogram') {
		switch (mode) {
			case 'mean':
				return 'Mean';
			case 'p75':
				return 'p75';
			case 'p95':
				return 'p95';
			case 'max':
				return 'Max (approx.)';
			default:
				return 'Percentiles';
		}
	}
	return 'Gauge';
}

// Rate mode changes the unit, not just the numbers ("By" -> "By/s") - same
// MetricChart.svelte `displayUnit` reasoning. Count mode is a plain sample count, not the
// metric's declared unit at all.
function modeUnit(type: string, mode: string, unit: string | null): string | null {
	if (type === 'Sum' && mode === 'count') return null;
	if (type === 'Sum' && mode === 'rate') return `${unit ?? ''}/s`;
	return unit;
}

function seriesLabel(series: MetricSeries): string {
	const attrs = Object.entries(series.attributes);
	if (attrs.length === 0) return series.serviceName;
	return `${series.serviceName} · ${attrs.map(([k, v]) => `${k}=${v}`).join(', ')}`;
}

function extractValues(points: MetricSeriesPoint[], type: string, mode: string, bucketWidthSeconds: number): (number | null)[] {
	switch (true) {
		case type === 'Sum' && mode === 'count':
			return points.map((p) => p.count);
		case type === 'Sum' && mode === 'rate':
			return points.map((p) => (p.value != null ? p.value / Math.max(bucketWidthSeconds, 1) : null));
		case type === 'Histogram' && mode === 'mean':
			return points.map((p) => (p.sum != null && p.count != null && p.count > 0 ? p.sum / p.count : null));
		case type === 'Histogram' && mode === 'p75':
			return points.map((p) => p.p75);
		case type === 'Histogram' && mode === 'p95':
			return points.map((p) => p.p95);
		case type === 'Histogram' && mode === 'max':
			return points.map((p) => p.maxApprox);
		default:
			return points.map((p) => p.value); // Gauge, and Sum's "sum" mode.
	}
}

/** One Unicode block char per point, quantized to 8 levels across the series' own min..max - downsampled to SPARK_WIDTH (even stride) for a long --since window. Direct TS counterpart of MetricCommand.cs's BuildSparkline. */
function buildSparkline(values: (number | null)[]): string {
	if (values.length === 0) return '';
	const sampled = values.length > SPARK_WIDTH ? downsample(values, SPARK_WIDTH) : values;

	const present = sampled.filter((v): v is number => v != null);
	if (present.length === 0) return ' '.repeat(sampled.length);

	const min = Math.min(...present);
	const max = Math.max(...present);
	const range = max - min;

	return sampled
		.map((v) => {
			if (v == null) return ' ';
			const level = range <= 0 ? Math.floor(SPARK_CHARS.length / 2) : Math.round(((v - min) / range) * (SPARK_CHARS.length - 1));
			return SPARK_CHARS[Math.min(Math.max(level, 0), SPARK_CHARS.length - 1)];
		})
		.join('');
}

function downsample(values: (number | null)[], targetCount: number): (number | null)[] {
	return Array.from({ length: targetCount }, (_, i) => {
		const index = Math.min(Math.floor((i * values.length) / targetCount), values.length - 1);
		return values[index];
	});
}

function formatRow(label: string, values: (number | null)[], unit: string | null, term: TerminalWriter): void {
	const spark = buildSparkline(values);
	const latest = [...values].reverse().find((v) => v != null) ?? null;
	const latestText = latest != null ? formatAtScale(latest, resolveAxisScale(unit, Math.abs(latest))) : '—';

	const plainLabel = label.length > 30 ? label.slice(0, 29) + '…' : label.padEnd(30);
	term.writeLine(`${plainLabel}  ${spark}  latest=${latestText}`, 'output');
}

export const metricCommand: TerminalCommand = {
	name: 'metric',
	summary: 'Charts one metric as ASCII sparklines (same data as the Metrics chart).',
	usage: 'metric <name> [-s|--service <name>] [--group-by <key>] [--mode <mode>] [--since <range>]',
	async run(args, term) {
		let parsed: ParsedArgs;
		try {
			parsed = parseArgs(args);
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
			return;
		}

		const to = new Date();
		const from = new Date(to.getTime() - parsed.sinceMs);

		let namesResponse;
		try {
			namesResponse = await getMetricNames({
				from: from.toISOString(),
				to: to.toISOString(),
				services: parsed.service ? [parsed.service] : undefined
			});
		} catch (err) {
			term.writeLine(`metric: ${err instanceof Error ? err.message : String(err)}`, 'error');
			return;
		}

		const candidates = namesResponse.metrics.filter((m) => m.metricName === parsed.metricName);
		if (candidates.length === 0) {
			term.writeLine(
				`No metric named '${parsed.metricName}' in the last ${parsed.sinceText}${parsed.service ? ` for service '${parsed.service}'` : ''} - try \`metrics\`.`,
				'info'
			);
			return;
		}
		if (candidates.length > 1) {
			const services = [...new Set(candidates.map((c) => c.serviceName))].join(', ');
			term.writeLine(`metric: '${parsed.metricName}' is emitted by more than one service - pick one with --service: ${services}`, 'error');
			return;
		}

		const metric = candidates[0];

		let mode: string;
		try {
			mode = resolveMode(metric.type, parsed.mode);
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
			return;
		}

		const bucketWidthSeconds = pickBucketWidthSeconds((to.getTime() - from.getTime()) / 1000);

		let queryResponse;
		try {
			queryResponse = await queryMetric({
				metricName: metric.metricName,
				type: metric.type,
				filter: { from: from.toISOString(), to: to.toISOString(), services: [metric.serviceName] },
				bucketWidthSeconds,
				groupByAttributeKey: parsed.groupBy
			});
		} catch (err) {
			term.writeLine(`metric: ${err instanceof Error ? err.message : String(err)}`, 'error');
			return;
		}

		const series = queryResponse.series;

		term.writeLine(`${metric.metricName} (${metric.type})`, 'info');
		if (metric.description) term.writeLine(metric.description, 'info');
		term.writeLine(`${series.length} series · ${formatBucketWidthSeconds(bucketWidthSeconds)} interval · mode: ${modeLabel(metric.type, mode)}`, 'info');
		term.writeLine('');

		if (series.length === 0) {
			term.writeLine('No data in range.', 'info');
			return;
		}

		for (const s of series.slice(0, MAX_SERIES)) {
			const points = [...s.points].sort((a, b) => a.bucketStart.localeCompare(b.bucketStart));
			if (metric.type === 'Histogram' && mode === 'percentiles') {
				term.writeLine(seriesLabel(s), 'info');
				formatRow('  p50', points.map((p) => p.p50), metric.unit, term);
				formatRow('  p90', points.map((p) => p.p90), metric.unit, term);
				formatRow('  p99', points.map((p) => p.p99), metric.unit, term);
				continue;
			}

			const values = extractValues(points, metric.type, mode, bucketWidthSeconds);
			formatRow(seriesLabel(s), values, modeUnit(metric.type, mode, metric.unit), term);
		}

		if (series.length > MAX_SERIES) {
			term.writeLine(`+${series.length - MAX_SERIES} more series not shown (${MAX_SERIES} max) - narrow --service/--group-by to see them.`, 'info');
		}
	}
};
