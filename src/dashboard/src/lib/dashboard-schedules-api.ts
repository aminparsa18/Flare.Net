// Client for Flare.Api's scheduled dashboard report API (`/api/dashboards/{id}/schedules`,
// `/api/dashboard-schedules/{id}`). JSON over the wire. See
// `docs-internal/adr/0142-scheduled-dashboard-reports.md`.

import { API_BASE_URL, apiFetch } from './api';

export type ReportFormat = 'pdf' | 'png';

export interface DashboardSchedule {
	id: string;
	dashboardId: string;
	name: string;
	enabled: boolean;
	/** Five-field cron expression, read in `timeZone`. */
	cron: string;
	timeZone: string;
	recipients: string;
	/** The dashboard's `?range=` preset, or '' for its own default. */
	timeRange: string;
	/** The dashboard's `var-<id>=...` query string, or ''. */
	variableQuery: string;
	format: ReportFormat;
	/** ISO instant of the next run; far in the future when the cron never fires again. */
	nextRunAt: string;
	createdAt: string;
	updatedAt: string;
}

export interface DashboardScheduleRequest {
	name: string;
	enabled: boolean;
	cron: string;
	timeZone: string;
	recipients: string;
	timeRange: string;
	variableQuery: string;
	format: ReportFormat;
}

export interface DashboardReportRun {
	id: string;
	scheduleId: string;
	startedAt: string;
	durationMs: number;
	status: 'Succeeded' | 'Failed';
	error: string;
	recipientCount: number;
	sizeBytes: number;
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

export async function listDashboardSchedules(dashboardId: string, signal?: AbortSignal): Promise<DashboardSchedule[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/dashboards/${dashboardId}/schedules`, { headers: { Accept: 'application/json' }, signal });
	if (!res.ok) throw await failure(res, 'GET schedules');
	return ((await res.json()) as { schedules?: DashboardSchedule[] }).schedules ?? [];
}

export async function createDashboardSchedule(dashboardId: string, request: DashboardScheduleRequest): Promise<DashboardSchedule> {
	const res = await apiFetch(`${API_BASE_URL}/api/dashboards/${dashboardId}/schedules`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, 'POST schedules');
	return (await res.json()) as DashboardSchedule;
}

export async function updateDashboardSchedule(id: string, request: DashboardScheduleRequest): Promise<DashboardSchedule> {
	const res = await apiFetch(`${API_BASE_URL}/api/dashboard-schedules/${id}`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, 'PUT schedule');
	return (await res.json()) as DashboardSchedule;
}

export async function deleteDashboardSchedule(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/dashboard-schedules/${id}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, 'DELETE schedule');
}

/** Marks the schedule due now; the worker sends it on its next tick. */
export async function sendDashboardScheduleNow(id: string): Promise<DashboardSchedule> {
	const res = await apiFetch(`${API_BASE_URL}/api/dashboard-schedules/${id}/send-now`, { method: 'POST', headers: { Accept: 'application/json' } });
	if (!res.ok) throw await failure(res, 'POST send-now');
	return (await res.json()) as DashboardSchedule;
}

export async function listDashboardReportRuns(id: string, signal?: AbortSignal): Promise<DashboardReportRun[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/dashboard-schedules/${id}/runs`, { headers: { Accept: 'application/json' }, signal });
	if (!res.ok) throw await failure(res, 'GET runs');
	return ((await res.json()) as { runs?: DashboardReportRun[] }).runs ?? [];
}
