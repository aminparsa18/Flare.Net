// `traces` - mimics flare.cli's TracesCommand.cs (root-span search) by calling the same
// POST /api/spans/search the dashboard's own Trace List uses (searchSpans() from
// $lib/traces-api.ts), rather than any new backend surface - same "already-authenticated
// dashboard API, different front end" shape as commands/tail.ts. Flag parsing/validation
// (status/kind names, duration/since suffix grammar) is a direct port of
// Flare.Cli/Commands/TracesCommand.cs's own Settings/TryExpand*/TryParse* so the two stay
// usable interchangeably.

import { searchSpans, type SpanDto, type SpanFilter } from '$lib/traces-api';
import { formatDurationNano } from '$lib/traces/duration';
import type { TerminalCommand } from '../types';

class UsageError extends Error {}

interface ParsedArgs {
	services: string[];
	statusCodes: string[];
	kinds: number[];
	traceId?: string;
	minDurationNano?: number;
	maxDurationNano?: number;
	sinceMs: number;
	limit: number;
}

const DEFAULT_SINCE_MS = 60 * 60_000;
const DEFAULT_LIMIT = 20;

function parseArgs(args: string[]): ParsedArgs {
	const result: ParsedArgs = { services: [], statusCodes: [], kinds: [], sinceMs: DEFAULT_SINCE_MS, limit: DEFAULT_LIMIT };

	for (let i = 0; i < args.length; i++) {
		const arg = args[i];
		switch (arg) {
			case '-s':
			case '--service':
				result.services.push(requireValue(args, ++i, arg));
				break;
			case '--status':
				result.statusCodes.push(parseStatus(requireValue(args, ++i, arg)));
				break;
			case '--kind':
				result.kinds.push(parseKind(requireValue(args, ++i, arg)));
				break;
			case '--trace-id':
				result.traceId = requireValue(args, ++i, arg);
				break;
			case '--min-duration':
				result.minDurationNano = parseDurationNano(requireValue(args, ++i, arg));
				break;
			case '--max-duration':
				result.maxDurationNano = parseDurationNano(requireValue(args, ++i, arg));
				break;
			case '--since':
				result.sinceMs = parseSince(requireValue(args, ++i, arg));
				break;
			case '-n':
			case '--limit': {
				const raw = requireValue(args, ++i, arg);
				const value = Number.parseInt(raw, 10);
				if (!Number.isFinite(value) || value <= 0) throw new UsageError(`traces: invalid --limit '${raw}'`);
				result.limit = value;
				break;
			}
			default:
				throw new UsageError(`traces: unrecognized option '${arg}'`);
		}
	}

	return result;
}

function requireValue(args: string[], index: number, flag: string): string {
	const value = args[index];
	if (value === undefined) throw new UsageError(`traces: ${flag} requires a value`);
	return value;
}

// Mirrors Flare.Cli's TracesCommand.cs TryExpandStatus.
function parseStatus(status: string): string {
	switch (status.trim().toLowerCase()) {
		case 'ok':
			return 'STATUS_CODE_OK';
		case 'error':
			return 'STATUS_CODE_ERROR';
		case 'unset':
			return 'STATUS_CODE_UNSET';
		default:
			throw new UsageError(`traces: unknown status '${status}' - expected one of: ok, error, unset`);
	}
}

// Mirrors Flare.Cli's TracesCommand.cs TryExpandKind (OTel Span.Kind, spec-fixed 0-5).
function parseKind(kind: string): number {
	switch (kind.trim().toLowerCase()) {
		case 'unspecified':
			return 0;
		case 'internal':
			return 1;
		case 'server':
			return 2;
		case 'client':
			return 3;
		case 'producer':
			return 4;
		case 'consumer':
			return 5;
		default:
			throw new UsageError(`traces: unknown kind '${kind}' - expected one of: internal, server, client, producer, consumer`);
	}
}

// Mirrors Flare.Cli's TracesCommand.cs TryParseDurationNano - a bare number (nanoseconds)
// or a number with a us/ms/s/m unit suffix.
function parseDurationNano(text: string): number {
	const match = text.trim().match(/^([0-9.]+)\s*(ns|us|µs|ms|s|m)?$/i);
	if (!match) throw new UsageError(`traces: couldn't parse duration '${text}' - expected e.g. 500ms, 2s, 1.5m`);
	const value = Number.parseFloat(match[1]);
	const unit = (match[2] ?? 'ns').toLowerCase();
	const multiplierByUnit: Record<string, number> = {
		ns: 1,
		us: 1_000,
		µs: 1_000,
		ms: 1_000_000,
		s: 1_000_000_000,
		m: 60 * 1_000_000_000
	};
	return Math.round(value * multiplierByUnit[unit]);
}

// Mirrors Flare.Cli's TracesCommand.cs TryParseSince - any magnitude, s/m/h/d suffix (not
// just the dashboard toolbar's 5 fixed presets, same reasoning that file's own comment
// gives: a CLI-style flag doesn't need a picklist).
function parseSince(text: string): number {
	const match = text.trim().match(/^([0-9.]+)([smhd])$/i);
	if (!match) throw new UsageError(`traces: couldn't parse --since '${text}' - expected e.g. 15m, 1h, 6h, 24h, 7d`);
	const value = Number.parseFloat(match[1]);
	const msByUnit: Record<string, number> = { s: 1_000, m: 60_000, h: 3_600_000, d: 86_400_000 };
	return value * msByUnit[match[2].toLowerCase()];
}

function statusLabel(statusCode: string): string {
	switch (statusCode) {
		case 'STATUS_CODE_OK':
			return 'OK';
		case 'STATUS_CODE_ERROR':
			return 'Error';
		default:
			return 'Unset';
	}
}

// Deliberately HH:mm:ss.fff only (no date), matching TracesCommand.cs's own row
// formatting - not tail.ts's formatTime (which prefixes month-day for log rows), a
// different command with a different real-CLI output to stay faithful to.
function formatTime(iso: string): string {
	const d = new Date(iso);
	const pad = (n: number, len = 2) => String(n).padStart(len, '0');
	return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${pad(d.getMilliseconds(), 3)}`;
}

function formatRow(span: SpanDto): string {
	const time = formatTime(span.startTime);
	const status = statusLabel(span.statusCode).padEnd(6);
	const service = (span.serviceName || '-').padEnd(20).slice(0, 20);
	const name = (span.name || '-').padEnd(30).slice(0, 30);
	const duration = formatDurationNano(span.durationNano).padStart(8);
	const spanCount = String(span.spanCount ?? 1).padStart(3);
	return `${time}  ${status}${service}${name}  ${duration}  ${spanCount}  ${span.traceId}`;
}

export const tracesCommand: TerminalCommand = {
	name: 'traces',
	summary: 'Searches recent traces (same feed as the Trace List).',
	usage:
		'traces [-s|--service <name>]... [--status <ok|error|unset>]... [--kind <kind>]... [--trace-id <id>] [--min-duration <d>] [--max-duration <d>] [--since <range>] [-n|--limit <count>]',
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

		const filter: SpanFilter = { from: from.toISOString(), to: to.toISOString(), rootSpansOnly: true };
		if (parsed.services.length > 0) filter.services = parsed.services;
		if (parsed.statusCodes.length > 0) filter.statusCodes = parsed.statusCodes;
		if (parsed.kinds.length > 0) filter.kinds = parsed.kinds;
		if (parsed.traceId) filter.traceId = parsed.traceId;
		if (parsed.minDurationNano !== undefined) filter.minDurationNano = parsed.minDurationNano;
		if (parsed.maxDurationNano !== undefined) filter.maxDurationNano = parsed.maxDurationNano;

		let response;
		try {
			response = await searchSpans({ filter, pageSize: Math.min(Math.max(parsed.limit, 1), 500) });
		} catch (err) {
			term.writeLine(`traces: ${err instanceof Error ? err.message : String(err)}`, 'error');
			return;
		}

		const traces = response.spans.slice(0, parsed.limit);
		if (traces.length === 0) {
			term.writeLine('No traces match the current filters.', 'info');
			return;
		}

		for (const span of traces) {
			term.writeLine(formatRow(span), span.statusCode === 'STATUS_CODE_ERROR' ? 'error' : 'output');
		}
		if (response.nextCursor && traces.length === response.spans.length) {
			term.writeLine(`… more available - narrow --since/--service or raise --limit (shown: ${traces.length}).`, 'info');
		}
	}
};
