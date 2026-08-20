// `metrics` - mimics flare.cli's MetricsCommand.cs (metric discovery) by calling the same
// POST /api/metrics/names the dashboard's own Metric Picker uses (getMetricNames() from
// $lib/metrics-api.ts), rather than any new backend surface - same "already-authenticated
// dashboard API, different front end" shape as commands/tail.ts/traces.ts.

import { getMetricNames, type MetricNameInfo } from '$lib/metrics-api';
import type { TerminalCommand } from '../types';

class UsageError extends Error {}

interface ParsedArgs {
	services: string[];
	sinceMs: number;
	limit: number;
}

const DEFAULT_SINCE_MS = 60 * 60_000;
const DEFAULT_LIMIT = 50;

function parseArgs(args: string[]): ParsedArgs {
	const result: ParsedArgs = { services: [], sinceMs: DEFAULT_SINCE_MS, limit: DEFAULT_LIMIT };

	for (let i = 0; i < args.length; i++) {
		const arg = args[i];
		switch (arg) {
			case '-s':
			case '--service':
				result.services.push(requireValue(args, ++i, arg));
				break;
			case '--since':
				result.sinceMs = parseSince(requireValue(args, ++i, arg));
				break;
			case '-n':
			case '--limit': {
				const raw = requireValue(args, ++i, arg);
				const value = Number.parseInt(raw, 10);
				if (!Number.isFinite(value) || value <= 0) throw new UsageError(`metrics: invalid --limit '${raw}'`);
				result.limit = value;
				break;
			}
			default:
				throw new UsageError(`metrics: unrecognized option '${arg}'`);
		}
	}

	return result;
}

function requireValue(args: string[], index: number, flag: string): string {
	const value = args[index];
	if (value === undefined) throw new UsageError(`metrics: ${flag} requires a value`);
	return value;
}

// Mirrors Flare.Cli's TracesCommand.cs TryParseSince, reused by MetricsCommand.cs/
// MetricCommand.cs there too.
export function parseSince(text: string): number {
	const match = text.trim().match(/^([0-9.]+)([smhd])$/i);
	if (!match) throw new UsageError(`metrics: couldn't parse --since '${text}' - expected e.g. 15m, 1h, 6h, 24h, 7d`);
	const value = Number.parseFloat(match[1]);
	const msByUnit: Record<string, number> = { s: 1_000, m: 60_000, h: 3_600_000, d: 86_400_000 };
	return value * msByUnit[match[2].toLowerCase()];
}

function formatRow(metric: MetricNameInfo): string {
	const name = metric.metricName.padEnd(36).slice(0, 60);
	const service = metric.serviceName.padEnd(20).slice(0, 20);
	const type = metric.type.padEnd(10);
	const unit = (metric.unit ?? '-').padEnd(8);
	return `${name}  ${service}${type}${unit}  ${metric.seriesCount} series`;
}

export const metricsCommand: TerminalCommand = {
	name: 'metrics',
	summary: 'Lists discoverable metrics (same feed as the Metric Picker).',
	usage: 'metrics [-s|--service <name>]... [--since <range>] [-n|--limit <count>]',
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

		let response;
		try {
			response = await getMetricNames({
				from: from.toISOString(),
				to: to.toISOString(),
				services: parsed.services.length > 0 ? parsed.services : undefined
			});
		} catch (err) {
			term.writeLine(`metrics: ${err instanceof Error ? err.message : String(err)}`, 'error');
			return;
		}

		const metrics = response.metrics.slice(0, parsed.limit);
		if (metrics.length === 0) {
			term.writeLine('No metrics found for the current filters.', 'info');
			return;
		}

		for (const metric of metrics) {
			term.writeLine(formatRow(metric), 'output');
		}
		if (response.metrics.length > parsed.limit) {
			term.writeLine(
				`… ${response.metrics.length - parsed.limit} more not shown - narrow --service/--since or raise --limit.`,
				'info'
			);
		}
	}
};
