// `export` - mimics flare.cli's ExportCommand.cs (dump a time range of log events) by
// reusing the Logs Explorer's own existing export pipeline ($lib/logs/export.ts -
// fetchAllForExport/eventsToBlob/exportFilename/downloadBlob), the same one
// ExportDialog.svelte already drives - no new fetch/pagination code here. Filter flags
// are identical to commands/search.ts's, reused from there rather than duplicated (same
// precedent commands/metric.ts/ingestion.ts already set by importing parseSince from
// commands/metrics.ts).
//
// Deliberately diverges from ExportCommand.cs's shape, not just its flags - a browser
// can't stream to stdout or an arbitrary host path:
//  - --format offers csv (default)/xlsx/json/xml (the dashboard's existing four
//    formats), not the CLI's ndjson/csv - there's no NDJSON writer on the browser side,
//    and inventing one solely for this command would duplicate eventsToJson's existing
//    (pretty-array) writer for no real benefit.
//  - No -o/--output - always triggers a real browser download via downloadBlob, the
//    natural browser analogue of the CLI writing a file.
//  - No --limit override - fetchAllForExport already hard-caps at EXPORT_ROW_CAP
//    (25,000, vs. the CLI's own 100,000 default) and that cap isn't parameterized in
//    the shared function, which is also used by the Logs Explorer's own dialog.

import type { LogFilter } from '$lib/api';
import { downloadBlob, eventsToBlob, exportFilename, fetchAllForExport, type ExportFormat } from '$lib/logs/export';
import type { TerminalCommand } from '../types';
import { buildLogFilter, parseLogFilterArgs, UsageError, type ParsedLogArgs } from './search';

const VALID_FORMATS: ExportFormat[] = ['csv', 'xlsx', 'json', 'xml'];

interface ExportArgs extends ParsedLogArgs {
	format: ExportFormat;
}

function parseExportArgs(args: string[]): ExportArgs {
	const { parsed, nextIndex } = parseLogFilterArgs(args, 'export');
	const result: ExportArgs = { ...parsed, format: 'csv' };

	for (let i = nextIndex; i < args.length; i++) {
		const arg = args[i];
		switch (arg) {
			case '--format': {
				const raw = args[++i];
				if (raw === undefined) throw new UsageError('export: --format requires a value');
				const lower = raw.toLowerCase();
				if (!VALID_FORMATS.includes(lower as ExportFormat)) {
					throw new UsageError(`export: unknown --format '${raw}' - expected one of: ${VALID_FORMATS.join(', ')}`);
				}
				result.format = lower as ExportFormat;
				break;
			}
			default:
				throw new UsageError(`export: unrecognized option '${arg}'`);
		}
	}

	return result;
}

export const exportCommand: TerminalCommand = {
	name: 'export',
	summary: 'Downloads a time range of log events as a file (csv/xlsx/json/xml).',
	usage:
		'export [-s|--service <name>]... [-l|--level <level>]... [--trace-id <id>] [--span-id <id>] [--pattern-id <id>] [--search <text>] [--since <range>] [--format csv|xlsx|json|xml]',
	async run(args, term) {
		let parsed: ExportArgs;
		let filter: LogFilter;
		try {
			parsed = parseExportArgs(args);
			filter = buildLogFilter(parsed, 'export');
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
			return;
		}

		term.writeLine('Fetching...', 'info');

		let result;
		try {
			result = await fetchAllForExport(filter);
		} catch (err) {
			term.writeLine(`export: ${err instanceof Error ? err.message : String(err)}`, 'error');
			return;
		}

		if (result.events.length === 0) {
			term.writeLine('No log events match the current filters - nothing to export.', 'info');
			return;
		}

		const blob = eventsToBlob(result.events, parsed.format);
		const filename = exportFilename({ from: filter.from!, to: filter.to! }, result.truncated, parsed.format, 'filtered');
		downloadBlob(blob, filename);

		term.writeLine(`Downloaded ${filename} (${result.events.length} row(s)).`, 'info');
		if (result.truncated) {
			term.writeLine(`Truncated at the export cap - narrow --since/--service to get everything.`, 'info');
		}
	}
};
