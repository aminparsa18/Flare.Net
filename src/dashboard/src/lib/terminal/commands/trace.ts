// `trace` - mimics flare.cli's TraceCommand.cs (single-trace waterfall) by calling the
// same GET /api/traces/{traceId} the dashboard's own trace-detail page uses (getTrace()
// from $lib/traces-api.ts), rather than any new backend surface - same
// "already-authenticated dashboard API, different front end" shape as commands/tail.ts.
// Tree-building/bar-positioning math is a direct port of TraceWaterfall.svelte's own
// `rows`/`barStyle` $derived - the same port TraceCommand.cs's C# already carries, kept
// here too since this terminal shares no code with the CLI. No critical-path highlighting
// or service-map here either, matching TraceCommand.cs's own current scope.

import { getTrace, type SpanDto } from '$lib/traces-api';
import { formatDurationNano } from '$lib/traces/duration';
import type { TerminalCommand, TerminalLineKind } from '../types';

const LABEL_WIDTH = 46;
const BAR_WIDTH = 36;

interface WaterfallRow {
	span: SpanDto;
	depth: number;
}

/** Direct port of TraceWaterfall.svelte's `rows` $derived - see its own remarks for the orphan-parent/cycle-guard rationale. */
function buildRows(spans: SpanDto[]): WaterfallRow[] {
	const spanIds = new Set(spans.map((s) => s.spanId));
	const byParent = new Map<string, SpanDto[]>();
	for (const span of spans) {
		const parentKey = span.parentSpanId && spanIds.has(span.parentSpanId) ? span.parentSpanId : '';
		const siblings = byParent.get(parentKey);
		if (siblings) siblings.push(span);
		else byParent.set(parentKey, [span]);
	}
	for (const siblings of byParent.values()) siblings.sort((a, b) => a.startTime.localeCompare(b.startTime));

	const result: WaterfallRow[] = [];
	const visited = new Set<string>();
	function visit(span: SpanDto, depth: number): void {
		if (visited.has(span.spanId)) return;
		visited.add(span.spanId);
		result.push({ span, depth });
		for (const child of byParent.get(span.spanId) ?? []) visit(child, depth + 1);
	}
	for (const root of byParent.get('') ?? []) visit(root, 0);
	return result;
}

// Mirrors TraceWaterfall.svelte's KIND_LABELS, abbreviated to fit a fixed-width column -
// same abbreviations TraceCommand.cs's KindTag uses.
function kindTag(kind: number): string {
	switch (kind) {
		case 1:
			return 'INT';
		case 2:
			return 'SRV';
		case 3:
			return 'CLI';
		case 4:
			return 'PRD';
		case 5:
			return 'CNS';
		default:
			return 'UNS';
	}
}

/** Port of TraceWaterfall.svelte's `barStyle` (left offset % / width %), quantized to BAR_WIDTH characters instead of CSS percentages. */
function buildBar(span: SpanDto, traceStartMs: number, totalMs: number): string {
	const startOffsetMs = new Date(span.startTime).getTime() - traceStartMs;
	const durationMs = Math.max(new Date(span.endTime).getTime() - new Date(span.startTime).getTime(), 0);

	const leftFrac = startOffsetMs / totalMs;
	// Same minimum-visible-width floor as barStyle's 0.5% - a near-zero-duration span
	// still renders at least one character instead of vanishing.
	const widthFrac = Math.max(durationMs / totalMs, 1 / BAR_WIDTH);

	const left = Math.min(Math.max(Math.round(leftFrac * BAR_WIDTH), 0), BAR_WIDTH - 1);
	const width = Math.min(Math.max(Math.round(widthFrac * BAR_WIDTH), 1), BAR_WIDTH - left);

	return ' '.repeat(left) + '█'.repeat(width) + ' '.repeat(BAR_WIDTH - left - width);
}

/** Port of TraceWaterfall.svelte's tick-header row (0/25/50/75/100% of totalMs), quantized to BAR_WIDTH characters. */
function buildAxisHeader(totalMs: number): string {
	const chars = new Array<string>(BAR_WIDTH).fill(' ');
	const fractions = [0, 0.25, 0.5, 0.75, 1];
	fractions.forEach((frac, i) => {
		const label = formatDurationNano(Math.round(frac * totalMs * 1_000_000));
		// Last tick right-aligns to the column's end (same as the CSS's -translate-x-full
		// on the final tick) so it doesn't overflow past the bar column.
		const pos = i === fractions.length - 1 ? Math.max(0, BAR_WIDTH - label.length) : Math.round(frac * BAR_WIDTH);
		for (let j = 0; j < label.length && pos + j < BAR_WIDTH; j++) chars[pos + j] = label[j];
	});
	return chars.join('');
}

function formatRow(span: SpanDto, depth: number, traceStartMs: number, totalMs: number): string {
	const indent = ' '.repeat(depth * 2);
	const name = span.name || '—';
	let label = `${indent}${kindTag(span.kind)} ${name} · ${span.serviceName}`;
	label = label.length > LABEL_WIDTH ? label.slice(0, LABEL_WIDTH - 1) + '…' : label.padEnd(LABEL_WIDTH);

	const bar = buildBar(span, traceStartMs, totalMs);
	const duration = formatDurationNano(span.durationNano).padStart(8);
	return `${label} ${bar} ${duration}`;
}

function pad(n: number, len = 2): string {
	return String(n).padStart(len, '0');
}

export const traceCommand: TerminalCommand = {
	name: 'trace',
	summary: 'Renders one trace as a text waterfall (same view as the trace-detail page).',
	usage: 'trace <traceId>',
	async run(args, term) {
		const traceId = args[0];
		if (!traceId) {
			term.writeLine('trace: a trace id is required - usage: trace <traceId>', 'error');
			return;
		}
		if (args.length > 1) {
			term.writeLine(`trace: unrecognized argument '${args[1]}'`, 'error');
			return;
		}

		let trace;
		try {
			trace = await getTrace(traceId);
		} catch (err) {
			term.writeLine(`trace: ${err instanceof Error ? err.message : String(err)}`, 'error');
			return;
		}

		if (trace === null || trace.spans.length === 0) {
			term.writeLine(`No spans found for trace ${traceId}.`, 'info');
			return;
		}

		const rows = buildRows(trace.spans);
		const traceStartMs = Math.min(...trace.spans.map((s) => new Date(s.startTime).getTime()));
		const traceEndMs = Math.max(...trace.spans.map((s) => new Date(s.endTime).getTime()));
		const totalMs = Math.max(1, traceEndMs - traceStartMs);
		const serviceCount = new Set(trace.spans.map((s) => s.serviceName)).size;
		const errorCount = trace.spans.filter((s) => s.statusCode === 'STATUS_CODE_ERROR').length;

		term.writeLine(`Trace ${trace.traceId}`, 'info');
		const started = new Date(traceStartMs);
		const startedLabel = `${pad(started.getHours())}:${pad(started.getMinutes())}:${pad(started.getSeconds())}.${pad(started.getMilliseconds(), 3)}`;
		const summary = `${trace.spans.length} span(s) · ${serviceCount} service(s) · ${formatDurationNano(totalMs * 1_000_000)} total · started ${startedLabel}`;
		const summaryKind: TerminalLineKind = errorCount > 0 ? 'error' : 'info';
		term.writeLine(errorCount > 0 ? `${summary} · ${errorCount} error(s)` : summary, summaryKind);
		term.writeLine('');

		term.writeLine(`${'Span'.padEnd(LABEL_WIDTH)} ${buildAxisHeader(totalMs)}`, 'info');
		term.writeLine('─'.repeat(LABEL_WIDTH + 1 + BAR_WIDTH + 1 + 8), 'info');

		for (const { span, depth } of rows) {
			term.writeLine(formatRow(span, depth, traceStartMs, totalMs), span.statusCode === 'STATUS_CODE_ERROR' ? 'error' : 'output');
		}
	}
};
