// Small formatting helpers shared by the Ingestion page's tiles/table/chart - no existing
// byte formatter anywhere else in the dashboard to reuse (every other page counts events,
// never bytes).

import type { IngestionProtocol, IngestionSignal } from '../ingestion-api';
import * as m from '$lib/paraglide/messages';

// Short protocol label, e.g. for a "Logs · gRPC" filter badge - distinct from the longer
// "gRPC :4317"/"Prometheus scrape" receiver-row labels IngestionReceivers.svelte/
// IngestionSignalsTable.svelte/the terminal `ingestion` command each keep as their own
// local per-surface array (same precedent 3 independent surfaces already followed before
// Scrape existed). This one centralizes the short form specifically because
// RejectedTelemetryDialog.svelte and IngestionLog.svelte (twice) were duplicating the
// exact same ternary before Scrape needed a third branch everywhere.
//
// Takes `string`, not `IngestionProtocol`, so it also accepts IngestionErrorEntry.protocol
// (recentErrors' own loosely-typed echo of whatever the backend sent - see
// toIngestionErrorEntry's `dto.protocol ?? ''` fallback) without narrowing first. An
// unrecognized value returns unchanged rather than throwing - the raw string is still more
// useful there than silently swallowing it.
//
// A function, not a module-scope lookup object - see time-range.ts's own remarks
// (surfaced during Phase 1) on why a plain object literal built once can't reflect a
// per-request/live-switched locale, only a call re-evaluated at each use can.
export function protocolLabel(protocol: IngestionProtocol | string): string {
	switch (protocol) {
		case 'Grpc':
			return m.ingestionProtocol_grpcShort();
		case 'Http':
			return m.ingestionProtocol_httpShort();
		case 'Scrape':
			return m.ingestionProtocol_scrapeShort();
		default:
			return protocol;
	}
}

// Signal display name (Logs/Traces/Metrics) - centralized here rather than left as the raw
// IngestionSignal string, since it shows up as prose ("Traces flush stale…" in
// ingestion/health.ts) as well as a bare label (chart legend, table cells), and both need
// the translated form. Takes `string` for the same IngestionErrorEntry.signal reason
// protocolLabel above does.
export function signalLabel(signal: IngestionSignal | string): string {
	switch (signal) {
		case 'Logs':
			return m.ingestionSignal_logs();
		case 'Traces':
			return m.ingestionSignal_traces();
		case 'Metrics':
			return m.ingestionSignal_metrics();
		default:
			return signal;
	}
}

const compactNumber = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 });

export function formatCount(n: number): string {
	return compactNumber.format(n);
}

const BYTE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const;

export function formatBytes(n: number): string {
	if (n <= 0) return '0 B';
	const exponent = Math.min(BYTE_UNITS.length - 1, Math.floor(Math.log(n) / Math.log(1024)));
	const value = n / 1024 ** exponent;
	// Every scaled unit already rounds (toFixed) - bytes themselves didn't, which was
	// invisible as long as every caller only ever passed whole byte counts, but a rate
	// (bytes/sec, see $lib/resources/format.ts's formatByteRate) is a division result and
	// can land under 1024 with a long fractional tail ("349.68019997311455 B/s") without
	// this.
	return `${exponent === 0 ? Math.round(value) : value.toFixed(value < 10 ? 1 : 0)} ${BYTE_UNITS[exponent]}`;
}

// Added for v10's pipeline-health section - "how long ago" for a last-flush timestamp or
// an oldest-pending-entry age, both of which arrive as seconds (or an ISO timestamp
// diffed against now), not a raw count like formatCount/formatBytes above.
export function formatAge(seconds: number | null | undefined): string {
	if (seconds === null || seconds === undefined) return '—';
	if (seconds < 1) return m.ingestionFormat_justNow();
	if (seconds < 60) return m.ingestionFormat_secondsAgo({ seconds: Math.floor(seconds) });
	if (seconds < 3600) return m.ingestionFormat_minutesAgo({ minutes: Math.floor(seconds / 60) });
	return m.ingestionFormat_hoursAgo({ hours: Math.floor(seconds / 3600) });
}

export function secondsSince(iso: string | null, now: Date = new Date()): number | null {
	if (!iso) return null;
	return Math.max(0, (now.getTime() - new Date(iso).getTime()) / 1000);
}

// Clock-skew display for the "Services by signal" table (ADR-0014). Sign follows
// PipelineServiceEntry.averageClockSkewMs: positive = this service's events typically
// arrive claiming a past time (expected: latency), negative = a future time (its clock
// is ahead of the server's). Below CLOCK_SKEW_WARN_MS (health.ts) this renders as "in
// sync" rather than a near-zero figure nobody needs to read - normal network/processing
// latency and minor NTP drift both land well under that threshold.
export function formatClockSkew(ms: number, warnThresholdMs: number): string {
	const abs = Math.abs(ms);
	if (abs < warnThresholdMs) return m.ingestionFormat_clockInSync();

	const ahead = ms < 0;
	if (abs < 1000) {
		const value = Math.round(abs);
		return ahead ? m.ingestionFormat_clockAheadMs({ ms: value }) : m.ingestionFormat_clockBehindMs({ ms: value });
	}
	if (abs < 60_000) {
		const value = Math.round(abs / 1000);
		return ahead ? m.ingestionFormat_clockAheadSeconds({ seconds: value }) : m.ingestionFormat_clockBehindSeconds({ seconds: value });
	}
	const value = Math.round(abs / 60_000);
	return ahead ? m.ingestionFormat_clockAheadMinutes({ minutes: value }) : m.ingestionFormat_clockBehindMinutes({ minutes: value });
}
