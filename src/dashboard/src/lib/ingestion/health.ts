// Overall "is ingestion healthy" verdict for the status indicator next to the Ingestion
// heading. Feedback: the page had five separate signals (arrivals, ingestion rate,
// rejected count, stream backlog, flush-worker errors) but no single answer to the first
// question anyone operating it actually has - the reader had to mentally combine them.
// Mirrors OpenTelemetry Collector's own health-signal guidance: accepted/refused data,
// exporter failures, and queue size/capacity are the core inputs - this combines exactly
// those three, already present on this page as totals.rejectedInWindow, flushWorkers[].
// consecutiveErrors/lastError, and streams[].pendingCount/lag/length/capacity.

import type { IngestionBucketPoint, IngestionSignal, IngestionStatsResponse } from '../ingestion-api';
import type { PipelineFlushHealth, PipelineStatsResponse, PipelineStreamHealth } from '../pipeline-api';
import { formatAge, formatCount } from './format';

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

// A worker that hasn't flushed in over a minute despite the signal actively receiving
// traffic is stale - ClickHouseFlushWorker/SpanFlushWorker/MetricFlushWorker all default to
// a 2s FlushInterval (a time-based trigger, independent of BatchSize), so nothing healthy
// legitimately goes anywhere near this long between flushes while there's anything to flush.
// Deliberately *not* keyed off isBacklogStuck alone (that needs a stream's own PEL to have
// pending entries) - a worker that's stopped calling XREADGROUP entirely never populates the
// PEL in the first place, so length just grows unnoticed there while this catches it via the
// one thing that's unambiguous either way: time since the last successful flush.
export const FLUSH_STALE_AGE_SECONDS = 60;

// How far back "currently receiving traffic" looks for the staleness check above -
// deliberately short and fixed, independent of the page's own selected window (a stuck
// worker should be caught quickly, not only once a multi-hour window happens to be selected).
export const RECENT_ACTIVITY_LOOKBACK_MINUTES = 3;

export function hasRecentArrivals(
	buckets: readonly Pick<IngestionBucketPoint, 'signal' | 'bucketStart' | 'records'>[],
	signal: IngestionSignal,
	now: Date = new Date()
): boolean {
	const cutoffMs = now.getTime() - RECENT_ACTIVITY_LOOKBACK_MINUTES * 60_000;
	return buckets.some((b) => b.signal === signal && b.records > 0 && new Date(b.bucketStart).getTime() >= cutoffMs);
}

// Feedback: a worker with consecutiveErrors=0 sitting next to a red lastError string read
// as "currently broken" - 0 consecutive errors is actually proof a *later* flush already
// succeeded, i.e. it recovered. computeFlushStatus separates "what's true right now" from
// "what happened historically" instead of letting the table imply the two are the same
// thing.
export type FlushStatusTone = 'good' | 'default' | 'warning' | 'destructive';

export interface FlushStatus {
	key: 'healthy' | 'recovered' | 'retrying' | 'stuck' | 'stale' | 'down' | 'idle';
	label: string;
	tone: FlushStatusTone;
}

/**
 * One worker's *current* state. `stream` (the matching signal's PipelineStreamHealth) is
 * optional and only used for the "stuck" case - cross-referencing the stream's own
 * pending-backlog signal (isBacklogStuck) rather than flagging on last-flush age alone, so
 * a worker with zero traffic legitimately going hours between flushes doesn't get flagged -
 * the exact false-alarm mistake PipelineStreamsTable's own Pending-column fix already
 * corrected once for the same underlying reason.
 *
 * `hasRecentTraffic` (from hasRecentArrivals, computed by the caller against stats.buckets)
 * gates the "stale" check below - the same false-alarm mistake in reverse: a 2h-old
 * lastFlushAt is completely normal for a signal with nothing to flush, and only becomes
 * suspicious once there's evidence something *should* have been flushed by now.
 */
export function computeFlushStatus(
	worker: Pick<PipelineFlushHealth, 'lastFlushAt' | 'lastError' | 'consecutiveErrors'>,
	stream?: Pick<PipelineStreamHealth, 'pendingCount' | 'oldestPendingAgeSeconds'>,
	hasRecentTraffic = false,
	now: Date = new Date()
): FlushStatus {
	if (worker.consecutiveErrors >= DOWN_CONSECUTIVE_ERRORS) {
		return { key: 'down', label: `Down (${formatCount(worker.consecutiveErrors)})`, tone: 'destructive' };
	}
	if (worker.consecutiveErrors > 0) {
		return { key: 'retrying', label: `Retrying (${formatCount(worker.consecutiveErrors)})`, tone: 'warning' };
	}
	if (stream && isBacklogStuck(stream)) {
		return { key: 'stuck', label: 'Stuck', tone: 'warning' };
	}
	if (hasRecentTraffic) {
		const ageSeconds = worker.lastFlushAt ? (now.getTime() - new Date(worker.lastFlushAt).getTime()) / 1000 : Infinity;
		if (ageSeconds >= FLUSH_STALE_AGE_SECONDS) {
			return { key: 'stale', label: 'Stale', tone: 'warning' };
		}
	}
	if (worker.lastError) {
		return { key: 'recovered', label: 'Recovered', tone: 'good' };
	}
	if (!worker.lastFlushAt) {
		return { key: 'idle', label: 'Idle', tone: 'default' };
	}
	return { key: 'healthy', label: 'Healthy', tone: 'good' };
}

// Feedback: a receiver with zero requests (e.g. nobody's using OTLP/HTTP, only gRPC) isn't
// a problem - it just means that transport is unused. Reuses FlushStatus's own tone
// vocabulary rather than inventing a second one (good/default/warning/destructive already
// says exactly what's needed here too).
export interface ReceiverStatus {
	key: 'healthy' | 'idle' | 'degraded' | 'down';
	label: string;
	tone: FlushStatusTone;
}

/** requests/rejected are the sum across all three signals for one protocol, within whatever window the caller's already querying. */
export function computeReceiverStatus(requests: number, rejected: number): ReceiverStatus {
	if (requests === 0) return { key: 'idle', label: 'Idle', tone: 'default' };
	if (rejected === 0) return { key: 'healthy', label: 'Healthy', tone: 'good' };
	if (rejected >= requests) return { key: 'down', label: 'Down', tone: 'destructive' }; // every request to this receiver failed - not a partial blip
	return { key: 'degraded', label: 'Degraded', tone: 'warning' };
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
	const workersStale = new Set<string>();
	for (const worker of pipeline.flushWorkers) {
		if (worker.consecutiveErrors >= DOWN_CONSECUTIVE_ERRORS) {
			workersDown.add(worker.signal);
			downReasons.push(`${worker.signal} exporter unavailable`);
			continue;
		}
		if (worker.consecutiveErrors > 0) {
			degradedReasons.push(`${worker.signal} flush retrying (${formatCount(worker.consecutiveErrors)} errors)`);
			continue;
		}
		// Feedback: "Traces last flush 2h ago" next to active traffic is exactly the kind of
		// thing this page shouldn't make the reader notice/interpret themselves - flag it the
		// same way computeFlushStatus's own "stale" case does, gated on hasRecentArrivals so
		// an idle signal's naturally-old lastFlushAt never gets flagged.
		if (hasRecentArrivals(stats.buckets, worker.signal)) {
			const ageSeconds = worker.lastFlushAt ? (Date.now() - new Date(worker.lastFlushAt).getTime()) / 1000 : Infinity;
			if (ageSeconds >= FLUSH_STALE_AGE_SECONDS) {
				workersStale.add(worker.signal);
				degradedReasons.push(`${worker.signal} flush stale (${formatAge(ageSeconds)} since last flush, still receiving traffic)`);
			}
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

		// A stale flush worker for this signal already explains a merely-*building* backlog
		// (the milder case - real capacity risk and PEL-stuck above still get their own
		// reason regardless) - don't report the same underlying cause twice.
		if (workersStale.has(stream.signal)) continue;

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