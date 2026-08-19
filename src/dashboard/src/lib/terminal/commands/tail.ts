// `tail` - the one flare.cli command this terminal can actually mimic, because
// flare.cli's own TailCommand does nothing more than open Flare.Api's
// GET /api/logs/tail WebSocket (see that file's doc comment: "the CLI-native
// equivalent of the dashboard's Logs Explorer live-tail"). This reuses the exact
// same client-side connection helper the Logs Explorer already uses, so there's no
// new backend surface at all - just a different front end for it.

import { connectLiveTail, type LogEventDto, type LogFilter } from '$lib/api';
import { SEVERITY_BUCKETS, severityNumbersForBucket } from '$lib/logs/severity';
import type { TerminalCommand, TerminalHandle, TerminalLineKind } from '../types';

// Flags mirror Flare.Cli/Commands/TailCommand.cs's Settings 1:1 (same short/long
// names, same repeatability) so anyone who knows the real CLI's `tail` already knows
// this one.
interface ParsedArgs {
	services: string[];
	levels: string[];
	traceId?: string;
	search?: string;
}

class UsageError extends Error {}

function parseArgs(args: string[]): ParsedArgs {
	const result: ParsedArgs = { services: [], levels: [] };

	for (let i = 0; i < args.length; i++) {
		const arg = args[i];
		switch (arg) {
			case '-s':
			case '--service':
				result.services.push(requireValue(args, ++i, arg));
				break;
			case '-l':
			case '--level':
				result.levels.push(requireValue(args, ++i, arg));
				break;
			case '--trace-id':
				result.traceId = requireValue(args, ++i, arg);
				break;
			case '--search':
				result.search = requireValue(args, ++i, arg);
				break;
			default:
				throw new UsageError(`tail: unrecognized option '${arg}'`);
		}
	}

	return result;
}

function requireValue(args: string[], index: number, flag: string): string {
	const value = args[index];
	if (value === undefined) throw new UsageError(`tail: ${flag} requires a value`);
	return value;
}

/** Case-insensitive level name -> exact SeverityNumber list, same bucketing the Logs Explorer's own severity filter uses. */
function severityNumbersForLevel(level: string): number[] {
	const bucket = SEVERITY_BUCKETS.find((b) => b.label.toLowerCase() === level.toLowerCase());
	if (!bucket) {
		const valid = SEVERITY_BUCKETS.map((b) => b.label.toLowerCase()).join(', ');
		throw new UsageError(`tail: unknown level '${level}' (expected one of: ${valid})`);
	}
	return severityNumbersForBucket(bucket);
}

function buildFilter(parsed: ParsedArgs): LogFilter {
	const filter: LogFilter = {};
	if (parsed.services.length > 0) filter.services = parsed.services;
	if (parsed.levels.length > 0) {
		filter.severityNumbers = [...new Set(parsed.levels.flatMap(severityNumbersForLevel))];
	}
	if (parsed.traceId) filter.traceId = parsed.traceId;
	if (parsed.search) filter.search = parsed.search;
	return filter;
}

function formatTime(iso: string): string {
	// Same hand-formatted convention as LogRow.svelte's own formatTime - kept in sync
	// deliberately so timestamps read identically here and in the Logs Explorer.
	const d = new Date(iso);
	const pad = (n: number, len = 2) => String(n).padStart(len, '0');
	return `${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`;
}

/** Terminal-line kind for a log event, coarser than severity.ts's 6 buckets/badge variants - just enough to color errors/warnings differently from the rest. */
function lineKindFor(severityNumber: number): TerminalLineKind {
	if (severityNumber >= 17) return 'error'; // Error, Fatal
	return 'output';
}

function formatEvent(event: LogEventDto): string {
	const level = (event.severityText || '?').toUpperCase().padEnd(5);
	return `${formatTime(event.timestamp)} [${level}] ${event.serviceName || '-'}: ${event.body}`;
}

export const tailCommand: TerminalCommand = {
	name: 'tail',
	summary: 'Streams live log events (same feed as the Logs Explorer).',
	usage: 'tail [-s|--service <name>]... [-l|--level <level>]... [--trace-id <id>] [--search <text>]',
	run(args, term): TerminalHandle {
		let parsed: ParsedArgs;
		let filter: LogFilter;
		try {
			parsed = parseArgs(args);
			filter = buildFilter(parsed);
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
			return { cancel() {} };
		}

		term.writeLine('Streaming live events - Ctrl+C to stop.', 'info');

		const connection = connectLiveTail(filter, {
			onStatusChange(status) {
				if (status === 'error') term.writeLine('tail: connection error', 'error');
				if (status === 'closed') term.writeLine('tail: connection closed', 'info');
			},
			onEvent(event: LogEventDto) {
				term.writeLine(formatEvent(event), lineKindFor(event.severityNumber));
			},
			onDropped(droppedCount) {
				term.writeLine(`[dropped ${droppedCount} events]`, 'info');
			},
			onError(error) {
				term.writeLine(`tail: ${error}`, 'error');
			}
		});

		return {
			cancel() {
				connection.close();
			}
		};
	}
};
