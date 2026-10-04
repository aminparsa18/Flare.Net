// `traces` - mimics flare.cli's TracesCommand.cs (root-span search) by calling the same
// POST /api/spans/search the dashboard's own Trace List uses (searchSpans() from
// $lib/traces-api.ts), rather than any new backend surface - same "already-authenticated
// dashboard API, different front end" shape as commands/tail.ts. Flag parsing/validation
// (status/kind names, duration/since suffix grammar) is a direct port of
// Flare.Cli/Commands/TracesCommand.cs's own Settings/TryExpand*/TryParse* so the two stay
// usable interchangeably. --attr/--attr-not/--attr-exists/--attr-absent (attr-flags.ts)
// mirror Flare.Cli's Internal/AttributeFlagParsing.cs the same way, always against the
// default Span bag (no --attr-bag flag yet).

import { searchSpans, type SpanAttributeFilter, type SpanDto, type SpanFilter, type SpanSortKey, type TraceSpanCondition } from '$lib/traces-api';
import { formatDurationNano } from '$lib/traces/duration';
import { parseAttrBareKey, parseAttrKeyValue, type AttrFlagEntry } from './attr-flags';
import { parseDurationNano } from './duration';
import { UsageError } from './usage-error';
import type { TerminalCommand } from '../types';
import { formatTimeOfDay } from '$lib/time/format';


interface ParsedArgs {
	services: string[];
	statusCodes: string[];
	kinds: number[];
	traceId?: string;
	attrs: AttrFlagEntry[];
	minDurationNano?: number;
	maxDurationNano?: number;
	entry: boolean;
	specs: string[];
	where?: string;
	sortBy: SpanSortKey;
	sortAscending: boolean;
	sinceMs: number;
	limit: number;
}

const DEFAULT_SINCE_MS = 60 * 60_000;
const DEFAULT_LIMIT = 20;

function parseArgs(args: string[]): ParsedArgs {
	const result: ParsedArgs = {
		services: [],
		statusCodes: [],
		kinds: [],
		attrs: [],
		entry: false,
		specs: [],
		sortBy: 'StartTime',
		sortAscending: false,
		sinceMs: DEFAULT_SINCE_MS,
		limit: DEFAULT_LIMIT
	};

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
			case '--attr':
				result.attrs.push(parseAttrKeyValue(requireValue(args, ++i, arg), arg, 'traces', 'Equals'));
				break;
			case '--attr-not':
				result.attrs.push(parseAttrKeyValue(requireValue(args, ++i, arg), arg, 'traces', 'NotEquals'));
				break;
			case '--attr-exists':
				result.attrs.push(parseAttrBareKey(requireValue(args, ++i, arg), arg, 'traces', 'Exists'));
				break;
			case '--attr-absent':
				result.attrs.push(parseAttrBareKey(requireValue(args, ++i, arg), arg, 'traces', 'Absent'));
				break;
			case '--min-duration':
				result.minDurationNano = parseDurationNano(requireValue(args, ++i, arg), 'traces', arg);
				break;
			case '--max-duration':
				result.maxDurationNano = parseDurationNano(requireValue(args, ++i, arg), 'traces', arg);
				break;
			case '--entry':
				result.entry = true;
				break;
			case '--span':
				result.specs.push(requireValue(args, ++i, arg));
				break;
			case '--where':
				result.where = requireValue(args, ++i, arg);
				break;
			case '--sort': {
				const raw = requireValue(args, ++i, arg);
				result.sortBy = parseSort(raw);
				break;
			}
			case '--asc':
				result.sortAscending = true;
				break;
			case '--since':
				result.sinceMs = parseSince(requireValue(args, ++i, arg));
				break;
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

// Mirrors Flare.Cli's TracesCommand.cs TryExpandSort - friendly --sort names onto SpanSortKey.
function parseSort(sort: string): SpanSortKey {
	switch (sort.trim().toLowerCase()) {
		case 'time':
			return 'StartTime';
		case 'duration':
			return 'Duration';
		case 'spans':
			return 'SpanCount';
		default:
			throw new UsageError(`traces: unknown --sort '${sort}' - expected one of: time, duration, spans`);
	}
}

// Mirrors Flare.Cli's TracesCommand.cs TryParseStructure - --span/--where into a
// SpanFilter.Structure. Only the spec syntax is checked here; the expression itself (and
// which letters it may use) is validated by the API, whose 400 detail searchSpans surfaces.
function parseStructure(specs: string[], where: string | undefined): { conditions: TraceSpanCondition[]; expression: string } {
	if (!where || where.trim().length === 0) throw new UsageError(`traces: --span needs --where, e.g. --where "A => B".`);
	if (specs.length === 0) throw new UsageError(`traces: --where needs at least one --span condition, e.g. --span "A:service=checkout".`);

	const conditions: TraceSpanCondition[] = [];
	for (const spec of specs) {
		const colon = spec.indexOf(':');
		if (colon <= 0) {
			throw new UsageError(`traces: couldn't parse --span '${spec}' - expected LETTER:key=value,..., e.g. A:service=checkout.`);
		}
		const condition: TraceSpanCondition = { name: spec.slice(0, colon).trim() };
		for (const pair of spec.slice(colon + 1).split(',').map((p) => p.trim()).filter((p) => p.length > 0)) {
			const eq = pair.indexOf('=');
			const key = eq > 0 ? pair.slice(0, eq).trim().toLowerCase() : '';
			const value = eq > 0 ? pair.slice(eq + 1).trim() : '';
			const fail = () =>
				new UsageError(`traces: couldn't parse '${pair}' in --span '${spec}' - keys are service, name, status (ok/error/unset), min-duration (e.g. 500ms).`);
			switch (key) {
				case 'service':
					condition.serviceName = value;
					break;
				case 'name':
					condition.spanName = value;
					break;
				case 'status':
					try {
						condition.statusCode = parseStatus(value);
					} catch {
						throw fail();
					}
					break;
				case 'min-duration':
					try {
						condition.minDurationNano = parseDurationNano(value, 'traces');
					} catch {
						throw fail();
					}
					break;
				default:
					throw fail();
			}
		}
		conditions.push(condition);
	}
	return { conditions, expression: where };
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

// Mirrors Flare.Cli's TracesCommand.cs RolledUpStatusCode (itself a port of
// $lib/traces/status.ts's rolledUpStatusCode - not imported directly, same
// keep-our-own-copy-in-lockstep reasoning this file's header comment gives for
// statusLabel/parseStatus/etc.). A root span's own statusCode only reflects the root, not
// the rest of the trace, so a trace whose root succeeded but has an erroring span deeper
// in the call chain would otherwise print/color as healthy. span.hasError is the
// server-computed rollup across every span in the trace (SpanDto.hasError) - when true and
// the root itself didn't already fail, this reports STATUS_CODE_ERROR instead.
function rolledUpStatusCode(span: SpanDto): string {
	if (span.hasError && span.statusCode !== 'STATUS_CODE_ERROR') return 'STATUS_CODE_ERROR';
	return span.statusCode;
}

// Deliberately HH:mm:ss.fff only (no date), matching TracesCommand.cs's own row
// formatting - not tail.ts's formatTime (which prefixes month-day for log rows), a
// different command with a different real-CLI output to stay faithful to.
function formatTime(iso: string): string {
	return formatTimeOfDay(iso, 'ms');
}

function formatRow(span: SpanDto): string {
	const time = formatTime(span.startTime);
	const status = statusLabel(rolledUpStatusCode(span)).padEnd(6);
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
		'traces [-s|--service <name>]... [--status <ok|error|unset>]... [--kind <kind>]... [--trace-id <id>] [--attr <key=value>]... [--attr-not <key=value>]... [--attr-exists <key>]... [--attr-absent <key>]... [--min-duration <d>] [--max-duration <d>] [--entry] [--span <spec>]... [--where <expr>] [--sort time|duration|spans] [--asc] [--since <range>] [--limit <count>]',
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

		const filter: SpanFilter = { from: from.toISOString(), to: to.toISOString(), rootSpansOnly: !parsed.entry };
		if (parsed.entry) filter.entrySpansOnly = true;
		if (parsed.services.length > 0) filter.services = parsed.services;
		if (parsed.statusCodes.length > 0) filter.statusCodes = parsed.statusCodes;
		if (parsed.kinds.length > 0) filter.kinds = parsed.kinds;
		if (parsed.traceId) filter.traceId = parsed.traceId;
		if (parsed.attrs.length > 0) {
			filter.attributes = parsed.attrs.map((a): SpanAttributeFilter => ({ bag: 'Span', key: a.key, value: a.value, operator: a.operator }));
		}
		if (parsed.minDurationNano !== undefined) filter.minDurationNano = parsed.minDurationNano;
		if (parsed.maxDurationNano !== undefined) filter.maxDurationNano = parsed.maxDurationNano;
		if (parsed.where !== undefined || parsed.specs.length > 0) {
			try {
				filter.structure = parseStructure(parsed.specs, parsed.where);
			} catch (err) {
				term.writeLine(err instanceof Error ? err.message : String(err), 'error');
				return;
			}
		}

		let response;
		try {
			response = await searchSpans({
				filter,
				pageSize: Math.min(Math.max(parsed.limit, 1), 500),
				sortBy: parsed.sortBy,
				sortAscending: parsed.sortAscending
			});
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
			term.writeLine(formatRow(span), rolledUpStatusCode(span) === 'STATUS_CODE_ERROR' ? 'error' : 'output');
		}
		if (response.nextCursor && traces.length === response.spans.length) {
			term.writeLine(`… more available - narrow --since/--service or raise --limit (shown: ${traces.length}).`, 'info');
		}
	}
};
