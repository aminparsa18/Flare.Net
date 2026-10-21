// Client for Flare.Api's check-in monitor API (`/api/checkin-monitors` CRUD). JSON over the wire.
// See `docs-internal/adr/0260-cron-job-checkins.md`.

import { API_BASE_URL, apiFetch } from './api';

export type CheckInState = 'Pending' | 'Ok' | 'Running' | 'Late' | 'Failed' | 'Overran' | 'Disabled';

export interface CheckInStatus {
	state: CheckInState;
	/** Newest start, ok or fail ping; null before the first. */
	lastPing: string | null;
	/** When the next ping is due, before the grace period; null when the schedule never fires again. */
	nextExpected: string | null;
	/** False while Late, Failed or Overran. */
	up: boolean;
}

export interface CheckInMonitor {
	id: string;
	/** The name a job puts in the ping URL. Fixed after creation. */
	slug: string;
	name: string;
	description: string;
	enabled: boolean;
	/** Five-field cron expression or macro; empty when the schedule is an interval. */
	cronExpression: string;
	/** Seconds between pings; 0 when the schedule is a cron expression. */
	intervalSeconds: number;
	timeZone: string;
	graceSeconds: number;
	/** 0 means no limit. */
	maxRuntimeSeconds: number;
	createdAt: string;
	updatedAt: string;
	/** Only present on list responses. */
	status?: CheckInStatus | null;
}

export interface CheckInMonitorRequest {
	name: string;
	/** Read on create only; blank derives it from the name. */
	slug?: string;
	description: string;
	enabled: boolean;
	/** Set exactly one of `cronExpression` and `intervalSeconds`. */
	cronExpression?: string;
	intervalSeconds?: number;
	timeZone?: string;
	graceSeconds: number;
	maxRuntimeSeconds: number;
}

export type CheckInRunOutcome = 'Ok' | 'Failed' | 'Running' | 'Unfinished';

/** One run of a job, paired from its start and finish pings. */
export interface CheckInRun {
	/** Null when the job only sends a finish ping. */
	startedAt: string | null;
	/** Null while running or when the run never finished. */
	finishedAt: string | null;
	/** Null unless both pings exist. */
	durationSeconds: number | null;
	outcome: CheckInRunOutcome;
	message: string;
}

async function failure(res: Response, what: string): Promise<Error> {
	let message = `${what} failed: ${res.status} ${res.statusText}`;
	try {
		const problem = await res.json();
		message = problem?.detail || problem?.title || message;
	} catch {
		// Not JSON - keep the generic message.
	}
	return new Error(message);
}

const jsonHeaders = { 'Content-Type': 'application/json', Accept: 'application/json' };

export async function listCheckInMonitors(signal?: AbortSignal): Promise<CheckInMonitor[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/checkin-monitors`, { headers: { Accept: 'application/json' }, signal });
	if (!res.ok) throw await failure(res, 'GET /api/checkin-monitors');
	return ((await res.json()) as { monitors?: CheckInMonitor[] }).monitors ?? [];
}

export async function createCheckInMonitor(request: CheckInMonitorRequest): Promise<CheckInMonitor> {
	const res = await apiFetch(`${API_BASE_URL}/api/checkin-monitors`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, 'POST /api/checkin-monitors');
	return (await res.json()) as CheckInMonitor;
}

export async function updateCheckInMonitor(id: string, request: CheckInMonitorRequest): Promise<CheckInMonitor> {
	const res = await apiFetch(`${API_BASE_URL}/api/checkin-monitors/${id}`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, `PUT /api/checkin-monitors/${id}`);
	return (await res.json()) as CheckInMonitor;
}

/** 204 No Content on success. */
export async function deleteCheckInMonitor(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/checkin-monitors/${id}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, `DELETE /api/checkin-monitors/${id}`);
}

/** The monitor's recent runs, newest first. */
export async function listCheckInRuns(id: string, limit = 50, signal?: AbortSignal): Promise<CheckInRun[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/checkin-monitors/${id}/runs?limit=${limit}`, { headers: { Accept: 'application/json' }, signal });
	if (!res.ok) throw await failure(res, `GET /api/checkin-monitors/${id}/runs`);
	return ((await res.json()) as { runs?: CheckInRun[] }).runs ?? [];
}

/** "12s", "3m 05s" or "1h 02m": runs last seconds to hours, so one unit would hide either end. */
export function formatRunDuration(seconds: number): string {
	if (seconds < 10) return `${seconds.toFixed(1)}s`;
	const whole = Math.round(seconds);
	if (whole < 60) return `${whole}s`;
	const pad = (n: number) => String(n).padStart(2, '0');
	if (whole < 3600) return `${Math.floor(whole / 60)}m ${pad(whole % 60)}s`;
	return `${Math.floor(whole / 3600)}h ${pad(Math.floor((whole % 3600) / 60))}m`;
}

/** The request that reproduces `monitor` unchanged, for the enable switch. */
export function requestFrom(monitor: CheckInMonitor, enabled: boolean = monitor.enabled): CheckInMonitorRequest {
	return {
		name: monitor.name,
		description: monitor.description,
		enabled,
		cronExpression: monitor.cronExpression || undefined,
		intervalSeconds: monitor.cronExpression ? undefined : monitor.intervalSeconds,
		timeZone: monitor.timeZone,
		graceSeconds: monitor.graceSeconds,
		maxRuntimeSeconds: monitor.maxRuntimeSeconds
	};
}

/** A shell line a job can run; the placeholders are the instance's ingest URL and an ingest key. */
export function pingCommand(slug: string): string {
	return `curl -fsS -m 10 -H "Authorization: Bearer <ingest-key>" "<ingest-url>/v1/checkins/${slug}"`;
}
