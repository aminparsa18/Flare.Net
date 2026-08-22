// `ingestion` - mimics flare.cli's IngestionCommand.cs, which calls the same
// GET /api/ingestion/stats + GET /api/ingestion/pipeline the dashboard's own Ingestion
// page uses. Unlike that C# hand-port (a different runtime, no code-sharing boundary
// with the dashboard), this one reuses ingestion/health.ts's computeIngestionHealth/
// computeReceiverStatus/computeFlushStatus directly - same TypeScript, same functions,
// zero risk of drifting from what the page itself shows.

import { getIngestionStats, type IngestionProtocol, type IngestionSignal } from '$lib/ingestion-api';
import { getPipelineStats } from '$lib/pipeline-api';
import { computeFlushStatus, computeIngestionHealth, computeReceiverStatus, hasRecentArrivals, isBacklogStuck, utilizationPercent } from '$lib/ingestion/health';
import { formatAge, formatBytes, formatCount, secondsSince } from '$lib/ingestion/format';
import type { TerminalCommand } from '../types';
import { parseSince } from './metrics';

class UsageError extends Error {}

interface ParsedArgs {
	sinceMs: number;
}

const DEFAULT_SINCE_MS = 60 * 60_000;

function parseArgs(args: string[]): ParsedArgs {
	const result: ParsedArgs = { sinceMs: DEFAULT_SINCE_MS };

	for (let i = 0; i < args.length; i++) {
		const arg = args[i];
		if (arg === '--since') {
			const raw = args[++i];
			if (raw === undefined) throw new UsageError('ingestion: --since requires a value');
			result.sinceMs = parseSince(raw);
		} else {
			throw new UsageError(`ingestion: unrecognized option '${arg}'`);
		}
	}

	return result;
}

const RECEIVERS: { value: IngestionProtocol; label: string }[] = [
	{ value: 'Grpc', label: 'gRPC :4317' },
	{ value: 'Http', label: 'HTTP :4318' },
	{ value: 'Scrape', label: 'Prometheus scrape' }
];

const SIGNALS: IngestionSignal[] = ['Logs', 'Traces', 'Metrics'];

export const ingestionCommand: TerminalCommand = {
	name: 'ingestion',
	summary: 'OTLP ingestion health: verdict, rates, receivers, pipeline (same page as Ingestion).',
	usage: 'ingestion [--since <range>]',
	async run(args, term) {
		let parsed: ParsedArgs;
		try {
			parsed = parseArgs(args);
		} catch (err) {
			term.writeLine(err instanceof Error ? err.message : String(err), 'error');
			return;
		}

		// Same [1, 1440] clamp IngestionStatsQueryService/PipelineQueryService apply
		// server-side - rounding here just keeps the request itself sane before it gets there.
		const minutes = Math.max(1, Math.min(1440, Math.round(parsed.sinceMs / 60_000)));

		let stats, pipeline;
		try {
			[stats, pipeline] = await Promise.all([getIngestionStats(minutes), getPipelineStats(minutes)]);
		} catch (err) {
			term.writeLine(`ingestion: ${err instanceof Error ? err.message : String(err)}`, 'error');
			return;
		}

		const health = computeIngestionHealth(stats, pipeline);
		if (health) {
			term.writeLine(`● ${health.label}`, health.level === 'down' ? 'error' : 'output');
			term.writeLine(health.detail, 'info');
		}

		const t = stats.totals;
		term.writeLine('', 'output');
		term.writeLine(
			`Ingress ${formatCount(t.arrivalsPerMinute)} req/min · Events ${formatCount(t.ingestedRecordsPerMinute)}/min · ` +
				`Data ${formatBytes(t.ingestedBytesPerMinute)}/min · Window ${formatCount(t.requestsInWindow)} req · Rejected ${formatCount(t.rejectedInWindow)}`,
			'output'
		);

		term.writeLine('', 'output');
		term.writeLine('Receivers', 'info');
		for (const { value, label } of RECEIVERS) {
			const matching = stats.buckets.filter((b) => b.protocol === value);
			const requests = matching.reduce((sum, b) => sum + b.requests, 0);
			const rejected = matching.reduce((sum, b) => sum + b.rejected, 0);
			const status = computeReceiverStatus(requests, rejected);
			term.writeLine(`  ${label.padEnd(14)}${status.label.padEnd(10)}${formatCount(requests)} req`, 'output');
		}

		term.writeLine('', 'output');
		term.writeLine('Pipeline', 'info');
		term.writeLine(
			`  ${'Signal'.padEnd(9)}${'Buffered'.padEnd(17)}${'Pending'.padEnd(9)}${'Last flush'.padEnd(12)}${'Status'.padEnd(12)}Last error`,
			'info'
		);

		const streamBySignal = new Map(pipeline.streams.map((s) => [s.signal, s]));
		const workerBySignal = new Map(pipeline.flushWorkers.map((w) => [w.signal, w]));

		for (const signal of SIGNALS) {
			const stream = streamBySignal.get(signal);
			const worker = workerBySignal.get(signal);

			const pct = stream ? utilizationPercent(stream) : null;
			const buffered = stream ? (pct !== null ? `${formatCount(stream.length)} (${pct}%)` : formatCount(stream.length)) : '—';
			const pending = stream ? formatCount(stream.pendingCount) + (isBacklogStuck(stream) ? '*' : '') : '—';
			const lastFlush = worker?.lastFlushAt ? formatAge(secondsSince(worker.lastFlushAt)) : 'never';
			const status = worker ? computeFlushStatus(worker, stream, hasRecentArrivals(stats.buckets, signal)) : null;
			const statusLabel = status?.label ?? 'Idle';
			const lastError = worker?.lastError ?? '—';

			term.writeLine(
				`  ${signal.padEnd(9)}${buffered.padEnd(17)}${pending.padEnd(9)}${lastFlush.padEnd(12)}${statusLabel.padEnd(12)}${lastError}`,
				status?.key === 'down' ? 'error' : 'output'
			);
		}
	}
};
