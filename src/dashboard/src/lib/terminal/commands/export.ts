// `export` - mimics flare.cli's ExportCommand.cs (dump a time range of log events, or an
// incident bundle around one trace) by reusing the Logs Explorer's own existing export
// pipeline ($lib/logs/export.ts - fetchAllForExport/eventsToBlob/exportFilename/downloadBlob),
// the same one ExportDialog.svelte already drives - no new fetch/pagination code here.
// Filter flags are identical to commands/search.ts's, reused from there rather than
// duplicated.
//
// A browser can't stream to stdout or an arbitrary host path, so the one deliberate
// divergence from ExportCommand.cs is where the bytes go: always a real browser download
// (downloadBlob), the natural analogue of the CLI writing a file. -o/--output therefore
// sets the downloaded file's name (any directory part is dropped). --format is the CLI's
// ndjson (default) or csv.
// --limit caps total rows exactly like the CLI (default 100000).
//
// --include-trace/--include-logs/--include-metrics build the same "incident bundle" zip the
// CLI does (manifest.json + trace.json/logs.ndjson/metrics.json), via terminal/zip.ts.

import type { LogFilter } from '$lib/api';
import { downloadBlob, eventsToBlob, eventsToNdjson, exportFilename, fetchAllForExport, type ExportFormat } from '$lib/logs/export';
import { pickBucketWidthSeconds } from '$lib/logs/bucket-width';
import { getMetricNames, queryMetric } from '$lib/metrics-api';
import { getTrace } from '$lib/traces-api';
import type { TerminalCommand, TerminalWriter } from '../types';
import { createZip, type ZipEntry } from '../zip';
import { buildLogFilter, parseLogFilterArgs, parseSince, UsageError, type ParsedLogArgs } from './search';

// Same two formats as ExportCommand.cs - the dashboard's xlsx/json/xml stay in the Logs Explorer's export dialog.
type TerminalExportFormat = 'csv' | 'ndjson';
const VALID_FORMATS: TerminalExportFormat[] = ['ndjson', 'csv'];

const DEFAULT_LIMIT = 100_000;
// Mirrors ExportCommand.cs's MaxBundleMetrics - keeps a trace touching many services from
// turning a "bundle this one incident" command into a full-fleet metrics dump.
const MAX_BUNDLE_METRICS = 100;

interface ExportArgs extends ParsedLogArgs {
	format: TerminalExportFormat;
	output?: string;
	limit: number;
	includeTrace: boolean;
	includeLogs: boolean;
	includeMetrics: boolean;
	marginMs: number;
}

function requireValue(args: string[], index: number, flag: string): string {
	const value = args[index];
	if (value === undefined) throw new UsageError(`export: ${flag} requires a value`);
	return value;
}

function parseExportArgs(args: string[]): ExportArgs {
	const { parsed, nextIndex } = parseLogFilterArgs(args, 'export');
	const result: ExportArgs = {
		...parsed,
		format: 'ndjson',
		limit: DEFAULT_LIMIT,
		includeTrace: false,
		includeLogs: false,
		includeMetrics: false,
		marginMs: 5 * 60_000
	};

	for (let i = nextIndex; i < args.length; i++) {
		const arg = args[i];
		switch (arg) {
			case '--format': {
				const raw = requireValue(args, ++i, arg);
				const lower = raw.toLowerCase();
				if (!VALID_FORMATS.includes(lower as TerminalExportFormat)) {
					throw new UsageError(`export: unknown --format '${raw}' - expected one of: ${VALID_FORMATS.join(', ')}`);
				}
				result.format = lower as TerminalExportFormat;
				break;
			}
			case '-o':
			case '--output':
				result.output = requireValue(args, ++i, arg);
				break;
			case '--limit': {
				const raw = requireValue(args, ++i, arg);
				const value = Number.parseInt(raw, 10);
				if (!Number.isFinite(value) || value <= 0) throw new UsageError(`export: invalid --limit '${raw}'`);
				result.limit = value;
				break;
			}
			case '--include-trace':
				result.includeTrace = true;
				break;
			case '--include-logs':
				result.includeLogs = true;
				break;
			case '--include-metrics':
				result.includeMetrics = true;
				break;
			case '--margin':
				result.marginMs = parseSince(requireValue(args, ++i, arg), 'export', '--margin');
				break;
			default:
				throw new UsageError(`export: unrecognized option '${arg}'`);
		}
	}

	return result;
}

/** A browser download can only name the file, not pick a directory - keep just the last path segment. */
function downloadName(output: string): string {
	return output.split(/[\\/]/).filter((part) => part.length > 0).pop() ?? output;
}

function formatBlob(events: Parameters<typeof eventsToBlob>[0], format: TerminalExportFormat): Blob {
	if (format === 'ndjson') return new Blob([eventsToNdjson(events)], { type: 'application/x-ndjson;charset=utf-8' });
	return eventsToBlob(events, format);
}

async function runLogExport(parsed: ExportArgs, filter: LogFilter, term: TerminalWriter): Promise<void> {
	term.writeLine('Fetching...', 'info');

	let result;
	try {
		result = await fetchAllForExport(filter, undefined, parsed.limit);
	} catch (err) {
		term.writeLine(`export: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	if (result.events.length === 0) {
		term.writeLine('No log events match the current filters - nothing to export.', 'info');
		return;
	}

	const blob = formatBlob(result.events, parsed.format);
	const filename = parsed.output
		? downloadName(parsed.output)
		: exportFilename({ from: filter.from!, to: filter.to! }, false, parsed.format as ExportFormat, 'filtered');
	downloadBlob(blob, filename);

	term.writeLine(`Downloaded ${filename} (${result.events.length} row(s)).`, 'info');
	if (result.truncated) {
		term.writeLine(`Stopped at --limit (${parsed.limit}). Raise --limit or narrow --since to export more.`, 'info');
	}
}

// Incident bundle: a zip with manifest.json (always) plus whichever of
// trace.json/logs.ndjson/metrics.json were asked for. The trace is fetched first purely to
// derive the archive's scope - the span-covered window (padded by --margin) and the
// distinct service list - same reasoning as ExportCommand.cs's ExecuteIncidentBundleAsync.
async function runIncidentBundle(parsed: ExportArgs, logFilter: LogFilter, term: TerminalWriter): Promise<void> {
	const traceId = parsed.traceId!;
	term.writeLine('Fetching...', 'info');

	let trace;
	try {
		trace = await getTrace(traceId);
	} catch (err) {
		term.writeLine(`export: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	const spans = trace?.spans ?? [];
	let windowFrom: Date;
	let windowTo: Date;
	let services: string[];
	if (spans.length > 0) {
		windowFrom = new Date(Math.min(...spans.map((s) => Date.parse(s.startTime))) - parsed.marginMs);
		windowTo = new Date(Math.max(...spans.map((s) => Date.parse(s.endTime))) + parsed.marginMs);
		services = [...new Set(spans.map((s) => s.serviceName))].sort();
	} else {
		term.writeLine(
			`No spans found for trace ${traceId} (already aged out of retention?) - falling back to --since for --include-logs; --include-metrics will find nothing without a resolved service list.`,
			'info'
		);
		windowTo = new Date();
		windowFrom = new Date(windowTo.getTime() - parsed.sinceMs);
		services = [];
	}

	const entries: ZipEntry[] = [
		{
			name: 'manifest.json',
			content: JSON.stringify(
				{
					traceId,
					generatedAt: new Date().toISOString(),
					from: windowFrom.toISOString(),
					to: windowTo.toISOString(),
					services,
					spanCount: spans.length,
					includesTrace: parsed.includeTrace,
					includesLogs: parsed.includeLogs,
					includesMetrics: parsed.includeMetrics
				},
				null,
				2
			)
		}
	];

	try {
		if (parsed.includeTrace) {
			entries.push({ name: 'trace.json', content: JSON.stringify(trace ?? { traceId, spans: [] }, null, 2) });
			term.writeLine(`trace.json: ${spans.length} span(s).`, 'info');
		}

		if (parsed.includeLogs) {
			const result = await fetchAllForExport(
				{ ...logFilter, from: windowFrom.toISOString(), to: windowTo.toISOString() },
				undefined,
				parsed.limit
			);
			entries.push({ name: 'logs.ndjson', content: eventsToNdjson(result.events) });
			term.writeLine(`logs.ndjson: ${result.events.length} row(s).`, 'info');
		}

		if (parsed.includeMetrics) {
			if (services.length === 0) {
				term.writeLine('metrics.json: skipped - no services resolved from the trace.', 'info');
			} else {
				const from = windowFrom.toISOString();
				const to = windowTo.toISOString();
				let names = (await getMetricNames({ from, to, services })).metrics;
				if (names.length > MAX_BUNDLE_METRICS) {
					term.writeLine(
						`metrics.json: ${names.length} metric(s) found across ${services.length} service(s) - keeping the first ${MAX_BUNDLE_METRICS} to bound the bundle's size.`,
						'info'
					);
					names = names.slice(0, MAX_BUNDLE_METRICS);
				}

				const bucketWidthSeconds = pickBucketWidthSeconds((windowTo.getTime() - windowFrom.getTime()) / 1000);
				const bundled = [];
				for (const metric of names) {
					const response = await queryMetric({
						metricName: metric.metricName,
						type: metric.type,
						filter: { from, to, services: [metric.serviceName] },
						bucketWidthSeconds
					});
					bundled.push({
						metricName: metric.metricName,
						serviceName: metric.serviceName,
						type: metric.type,
						unit: metric.unit,
						description: metric.description,
						bucketWidthSeconds,
						series: response.series
					});
				}
				entries.push({ name: 'metrics.json', content: JSON.stringify(bundled, null, 2) });
				term.writeLine(`metrics.json: ${bundled.length} metric(s) across ${services.length} service(s).`, 'info');
			}
		}
	} catch (err) {
		term.writeLine(`export: Couldn't build the incident bundle: ${err instanceof Error ? err.message : String(err)}`, 'error');
		return;
	}

	const filename = downloadName(parsed.output!);
	downloadBlob(createZip(entries), filename);
	term.writeLine(`Wrote incident bundle to ${filename}.`, 'info');
}

export const exportCommand: TerminalCommand = {
	name: 'export',
	summary: 'Downloads a time range of log events (or an incident bundle) as a file.',
	usage:
		'export [-s|--service <name>]... [-l|--level <level>]... [--trace-id <id>] [--span-id <id>] [--pattern-id <id>] [--search <text>] [--search-all-fields] [--attr <key=value>]... [--attr-not <key=value>]... [--attr-exists <key>]... [--attr-absent <key>]... [--trace-span-service <name>]... [--trace-span-error] [--trace-span-name <name>]... [--trace-span-min-duration <d>] [--since <range>] [--format ndjson|csv] [-o|--output <name>] [--limit <count>] [--include-trace] [--include-logs] [--include-metrics] [--margin <range>]',
	async run(args, term) {
		let parsed: ExportArgs;
		let filter: LogFilter;
		try {
			parsed = parseExportArgs(args);
			filter = buildLogFilter(parsed, 'export');
			if (parsed.includeTrace || parsed.includeLogs || parsed.includeMetrics) {
				if (!parsed.traceId?.trim()) {
					throw new UsageError('export: --include-trace/--include-logs/--include-metrics require --trace-id - an incident bundle is keyed to one trace.');
				}
				if (!parsed.output?.trim()) {
					throw new UsageError('export: --include-trace/--include-logs/--include-metrics need -o|--output <name> - the bundle is downloaded as a zip file.');
				}
			}
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
			return;
		}

		if (parsed.includeTrace || parsed.includeLogs || parsed.includeMetrics) {
			return runIncidentBundle(parsed, filter, term);
		}
		return runLogExport(parsed, filter, term);
	}
};
