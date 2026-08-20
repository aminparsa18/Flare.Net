// Overall "is ingestion healthy" verdict for the status indicator next to the Ingestion
// heading. Feedback: the page had five separate signals (arrivals, ingestion rate,
// rejected count, stream backlog, flush-worker errors) but no single answer to the first
// question anyone operating it actually has - the reader had to mentally combine them.
// Mirrors OpenTelemetry Collector's own health-signal guidance: accepted/refused data,
// exporter failures, and queue size/capacity are the core inputs - this combines exactly
// those three, already present on this page as totals.rejectedInWindow, flushWorkers[].
// consecutiveErrors/lastError, and streams[].pendingCount/lag/length/capacity.

import type { IngestionStatsResponse } from '../ingestion-api';
import type { PipelineStatsResponse, PipelineStreamHealth } from '../pipeline-api';
import { formatCount } from './format';

export type IngestionHealthLevel = 'healthy' | 'degraded' | 'down';

export interface IngestionHealth {
	level: IngestionHealthLevel;
	label: string;
	detail: string;
}

// A flush worker failing this many times in a row reads as "down" (exporter/sink
// unreachable) rather than "degraded" (a transient blip) - one error alone shouldn't flip
// the whole page red, since ClickHouseFlushWorker retries with backoff and often recovers
// within a cycle or two. Exported so IngestionTopology's worker nodes use the same bar as
// the page-level verdict instead of picking their own.
export const DOWN_CONSECUTIVE_ERRORS = 3;

// A stream's pending (unacked) entries only mean "stuck", not just "buffered", once
// they've sat unconsumed for a while - a few seconds of pending entries is normal
// steady-state consumer lag, not a problem. Exported so PipelineStreamsTable's own Pending
// cell can apply the same bar instead of flagging *any* nonzero count - pending entries are
// just-arrived, successfully-parsed events awaiting an ack, not a failure of any kind.
export const STUCK_BACKLOG_AGE_SECONDS = 5 * 60;

export function isBacklogStuck(stream: Pick<PipelineStreamHealth, 'pendingCount' | 'oldestPendingAgeSeconds'>): boolean {
	return stream.pendingCount > 0 && (stream.oldestPendingAgeSeconds ?? 0) >= STUCK_BACKLOG_AGE_SECONDS;
}

// OpenTelemetry's own collector-health guidance calls ~60-70% queue utilization "a useful
// point to start considering scaling" - used here as the "degraded" threshold, and also as
// PipelineStreamsTable's bar-color cutoff so the table and the header verdict agree on what
// "getting full" means.
export const WARN_UTILIZATION_PERCENT = 70;

// Redis's approximate MAXLEN trim (see PipelineStreamHealth.capacity's remarks) drops the
// *oldest* unflushed entries once a stream actually hits its cap - real data loss, not just
// backpressure, so this is a "down" condition rather than merely "degraded".
export const DOWN_UTILIZATION_PERCENT = 90;

export function utilizationPercent(stream: Pick<PipelineStreamHealth, 'length' | 'capacity'>): number | null {
	return stream.capacity > 0 ? Math.round((stream.length / stream.capacity) * 100) : null;
}

export function computeIngestionHealth(
	stats: IngestionStatsResponse | null,
	pipeline: PipelineStatsResponse | null
): IngestionHealth | null {
	if (!stats || !pipeline) return null;

	const totals = stats.totals;
	const rejectionRate = totals.requestsInWindow > 0 ? totals.rejectedInWindow / totals.requestsInWindow : 0;

	const downReasons: string[] = [];
	const degradedReasons: string[] = [];

	const workersDown = new Set<string>();
	for (const worker of pipeline.flushWorkers) {
		if (worker.consecutiveErrors >= DOWN_CONSECUTIVE_ERRORS) {
			workersDown.add(worker.signal);
			downReasons.push(`${worker.signal} exporter unavailable`);
		} else if (worker.consecutiveErrors > 0) {
			degradedReasons.push(`${worker.signal} flush retrying (${formatCount(worker.consecutiveErrors)} errors)`);
		}
	}

	for (const stream of pipeline.streams) {
		if (!stream.available) continue; // no traffic yet for this signal - not a failure
		const pct = utilizationPercent(stream);

		// A down flush worker for this signal already explains the backlog - don't report
		// the same underlying failure as two separate reasons, just fold in the size/%.
		if (workersDown.has(stream.signal)) {
			if (stream.length > 0) {
				downReasons.push(pct !== null ? `${formatCount(stream.length)} buffered (${pct}%)` : `${formatCount(stream.length)} buffered`);
			}
			continue;
		}

		if (pct !== null && pct >= DOWN_UTILIZATION_PERCENT) {
			downReasons.push(`${stream.signal} buffer at ${pct}% capacity, oldest entries at risk of being dropped`);
			continue;
		}

		if (isBacklogStuck(stream)) {
			downReasons.push(`${stream.signal} pipeline backlog stuck (${formatCount(stream.pendingCount)} pending)`);
			continue;
		}

		if (pct !== null && pct >= WARN_UTILIZATION_PERCENT) {
			degradedReasons.push(`${stream.signal} buffer at ${pct}% capacity`);
		} else if (stream.pendingCount > 0 || (stream.lag ?? 0) > 0) {
			degradedReasons.push(`${stream.signal} backlog building (${formatCount(stream.length)} buffered)`);
		}
	}

	if (totals.rejectedInWindow > 0) {
		degradedReasons.push(`${Math.round(rejectionRate * 100)}% rejected (${formatCount(totals.rejectedInWindow)})`);
	}

	if (downReasons.length > 0) {
		return { level: 'down', label: 'Down', detail: [...downReasons, ...degradedReasons].slice(0, 2).join(' · ') };
	}
	if (degradedReasons.length > 0) {
		return { level: 'degraded', label: 'Degraded', detail: degradedReasons.slice(0, 2).join(' · ') };
	}
	return {
		level: 'healthy',
		label: 'Healthy',
		detail: 'All receivers operational · 0% rejected · no pipeline backlog'
	};
}