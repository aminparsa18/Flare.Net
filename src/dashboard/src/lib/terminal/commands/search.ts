// `search` - mimics flare.cli's SearchCommand.cs (one-shot log search) by calling the
// same POST /api/logs/search the Logs Explorer itself uses (searchLogs() from $lib/api),
// rather than any new backend surface - same "already-authenticated dashboard API,
// different front end" shape as commands/tail.ts/traces.ts. Flag parsing is a direct port
// of Flare.Cli/Commands/SearchCommand.cs's own Settings. Exports its parse helpers so
// commands/export.ts (identical filter-flag set) reuses them rather than a third copy -
// same precedent commands/metrics.ts's parseSince already sets for metric.ts/ingestion.ts.

import { searchLogs, type LogEventDto, type LogFilter } from '$lib/api';
import { SEVERITY_BUCKETS, severityNumbersForBucket } from '$lib/logs/severity';
import type { TerminalCommand } from '../types';

export class UsageError extends Error {}

export interface ParsedLogArgs {
	services: string[];
	levels: string[];
	traceId?: string;
	spanId?: string;
	patternId?: string;
	search?: string;
	sinceMs: number;
}

const DEFAULT_SINCE_MS = 60 * 60_000;
const DEFAULT_LIMIT = 20;

/** Shared by search.ts/export.ts - both take the same filter flags, only search.ts also has -n/--limit. */
export function parseLogFilterArgs(args: string[], commandName: string, startIndex = 0): { parsed: ParsedLogArgs; nextIndex: number } {
	const result: ParsedLogArgs = { services: [], levels: [], sinceMs: DEFAULT_SINCE_MS };
	let i = startIndex;

	for (; i < args.length; i++) {
		const arg = args[i];
		switch (arg) {
			case '-s':
			case '--service':
				result.services.push(requireValue(args, ++i, arg, commandName));
				break;
			case '-l':
			case '--level':
				result.levels.push(requireValue(args, ++i, arg, commandName));
				break;
			case '--trace-id':
				result.traceId = requireValue(args, ++i, arg, commandName);
				break;
			case '--span-id':
				result.spanId = requireValue(args, ++i, arg, commandName);
				break;
			case '--pattern-id':
				result.patternId = requireValue(args, ++i, arg, commandName);
				break;
			case '--search':
				result.search = requireValue(args, ++i, arg, commandName);
				break;
			case '--since':
				result.sinceMs = parseSince(requireValue(args, ++i, arg, commandName), commandName);
				break;
			default:
				return { parsed: result, nextIndex: i };
		}
	}

	return { parsed: result, nextIndex: i };
}

function requireValue(args: string[], index: number, flag: string, commandName: string): string {
	const value = args[index];
	if (value === undefined) throw new UsageError(`${commandName}: ${flag} requires a value`);
	return value;
}

/** Case-insensitive level name -> exact SeverityNumber list, same bucketing the Logs Explorer's own severity filter uses. */
export function severityNumbersForLevel(level: string, commandName: string): number[] {
	const bucket = SEVERITY_BUCKETS.find((b) => b.label.toLowerCase() === level.toLowerCase());
	if (!bucket) {
		const valid = SEVERITY_BUCKETS.map((b) => b.label.toLowerCase()).join(', ');
		throw new UsageError(`${commandName}: unknown level '${level}' (expected one of: ${valid})`);
	}
	return severityNumbersForBucket(bucket);
}

// Mirrors Flare.Cli's TracesCommand.cs/SearchCommand.cs TryParseSince - any magnitude,
// s/m/h/d suffix.
export function parseSince(text: string, commandName: string): number {
	const match = text.trim().match(/^([0-9.]+)([smhd])$/i);
	if (!match) throw new UsageError(`${commandName}: couldn't parse --since '${text}' - expected e.g. 15m, 1h, 6h, 24h, 7d`);
	const value = Number.parseFloat(match[1]);
	const msByUnit: Record<string, number> = { s: 1_000, m: 60_000, h: 3_600_000, d: 86_400_000 };
	return value * msByUnit[match[2].toLowerCase()];
}

export function buildLogFilter(parsed: ParsedLogArgs, commandName: string): LogFilter {
	const to = new Date();
	const from = new Date(to.getTime() - parsed.sinceMs);
	const filter: LogFilter = { from: from.toISOString(), to: to.toISOString() };
	if (parsed.services.length > 0) filter.services = parsed.services;
	if (parsed.levels.length > 0) {
		filter.severityNumbers = [...new Set(parsed.levels.flatMap((l) => severityNumbersForLevel(l, commandName)))];
	}
	if (parsed.traceId) filter.traceId = parsed.traceId;
	if (parsed.spanId) filter.spanId = parsed.spanId;
	if (parsed.patternId) filter.patternId = parsed.patternId;
	if (parsed.search) filter.search = parsed.search;
	return filter;
}

interface SearchArgs extends ParsedLogArgs {
	limit: number;
}

function parseSearchArgs(args: string[]): SearchArgs {
	const { parsed, nextIndex } = parseLogFilterArgs(args, 'search');
	const result: SearchArgs = { ...parsed, limit: DEFAULT_LIMIT };

	for (let i = nextIndex; i < args.length; i++) {
		const arg = args[i];
		switch (arg) {
			case '-n':
			case '--limit': {
				const raw = requireValue(args, ++i, arg, 'search');
				const value = Number.parseInt(raw, 10);
				if (!Number.isFinite(value) || value <= 0) throw new UsageError(`search: invalid --limit '${raw}'`);
				result.limit = value;
				break;
			}
			default:
				throw new UsageError(`search: unrecognized option '${arg}'`);
		}
	}

	return result;
}

// Same coarse error/output split tail.ts's lineKindFor uses (severityNumber >= 17 =
// Error/Fatal), not a full 6-bucket mapping - just enough to color errors differently.
function lineKindFor(severityNumber: number): 'output' | 'error' {
	return severityNumber >= 17 ? 'error' : 'output';
}

function formatTime(iso: string): string {
	const d = new Date(iso);
	const pad = (n: number, len = 2) => String(n).padStart(len, '0');
	return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`;
}

function truncate(text: string, maxLength: number): string {
	return text.length <= maxLength ? text : text.slice(0, maxLength - 1) + '…';
}

function formatRow(event: LogEventDto): string {
	const time = formatTime(event.timestamp);
	const level = (event.severityText || '?').toUpperCase().padEnd(6);
	const service = (event.serviceName || '-').padEnd(20).slice(0, 20);
	const message = truncate(event.body, 60).padEnd(60);
	return `${time}  ${level}${service}  ${message}  ${event.traceId}`;
}

export const searchCommand: TerminalCommand = {
	name: 'search',
	summary: 'One-shot log search (same feed as the Logs Explorer).',
	usage:
		'search [-s|--service <name>]... [-l|--level <level>]... [--trace-id <id>] [--span-id <id>] [--pattern-id <id>] [--search <text>] [--since <range>] [-n|--limit <count>]',
	async run(args, term) {
		let parsed: SearchArgs;
		try {
			parsed = parseSearchArgs(args);
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
			return;
		}

		let filter: LogFilter;
		try {
			filter = buildLogFilter(parsed, 'search');
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
			return;
		}

		let response;
		try {
			response = await searchLogs({ filter, pageSize: Math.min(Math.max(parsed.limit, 1), 500) });
		} catch (err) {
			term.writeLine(`search: ${err instanceof Error ? err.message : String(err)}`, 'error');
			return;
		}

		const events = response.events.slice(0, parsed.limit);
		if (events.length === 0) {
			term.writeLine('No log events match the current filters.', 'info');
			return;
		}

		for (const event of events) {
			term.writeLine(formatRow(event), lineKindFor(event.severityNumber));
		}
		if (response.nextCursor && events.length === response.events.length) {
			term.writeLine(`… more available - narrow --since/--service or raise --limit (shown: ${events.length}).`, 'info');
		}
	}
};
