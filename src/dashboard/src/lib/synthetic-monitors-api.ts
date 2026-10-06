// Client for Flare.Api's synthetic monitor API (`/api/synthetic-monitors` CRUD). JSON over the wire.
// See `docs-internal/adr/0128-synthetic-monitoring.md`.

import { API_BASE_URL, apiFetch } from './api';

export type SyntheticMonitorKind = 'Http' | 'Tcp' | 'Tls' | 'Dns' | 'Udp' | 'Icmp';

export interface SyntheticMonitorStatus {
	up: boolean;
	time: string;
	durationMs: number | null;
	httpStatus: number | null;
	certExpiryDays: number | null;
}

export interface SyntheticLocationStatus {
	location: string;
	status: SyntheticMonitorStatus;
}

export interface SyntheticMonitor {
	id: string;
	name: string;
	description: string;
	enabled: boolean;
	kind: SyntheticMonitorKind;
	target: string;
	method: string;
	/** 0 means any 2xx or 3xx. */
	expectedStatus: number;
	/** One `Name: value` header per line. */
	requestHeaders: string;
	requestBody: string;
	bodyContains: string;
	bodyNotContains: string;
	bodyMatchesRegex: string;
	jsonPath: string;
	jsonPathEquals: string;
	/** Dns: an IP the answer must include. Udp: text the reply must contain. */
	expectedAnswer: string;
	intervalSeconds: number;
	timeoutSeconds: number;
	/** Probe locations that run this monitor; empty means every location. */
	locations: string[];
	createdAt: string;
	updatedAt: string;
	/** Most recent probe result; only present on list responses, and absent until a probe has run. */
	latest?: SyntheticMonitorStatus | null;
	/** Latest result per location; only present on list responses. */
	locationStatuses?: SyntheticLocationStatus[];
}

export interface SyntheticMonitorRequest {
	name: string;
	description: string;
	enabled: boolean;
	kind: SyntheticMonitorKind;
	target: string;
	method: string;
	expectedStatus: number;
	requestHeaders: string;
	requestBody: string;
	bodyContains: string;
	bodyNotContains: string;
	bodyMatchesRegex: string;
	jsonPath: string;
	jsonPathEquals: string;
	expectedAnswer: string;
	intervalSeconds: number;
	timeoutSeconds: number;
	locations: string[];
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

export async function listSyntheticMonitors(signal?: AbortSignal): Promise<SyntheticMonitor[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/synthetic-monitors`, { headers: { Accept: 'application/json' }, signal });
	if (!res.ok) throw await failure(res, 'GET /api/synthetic-monitors');
	return ((await res.json()) as { monitors?: SyntheticMonitor[] }).monitors ?? [];
}

export async function createSyntheticMonitor(request: SyntheticMonitorRequest): Promise<SyntheticMonitor> {
	const res = await apiFetch(`${API_BASE_URL}/api/synthetic-monitors`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, 'POST /api/synthetic-monitors');
	return (await res.json()) as SyntheticMonitor;
}

export async function updateSyntheticMonitor(id: string, request: SyntheticMonitorRequest): Promise<SyntheticMonitor> {
	const res = await apiFetch(`${API_BASE_URL}/api/synthetic-monitors/${id}`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, `PUT /api/synthetic-monitors/${id}`);
	return (await res.json()) as SyntheticMonitor;
}

/** 204 No Content on success. */
export async function deleteSyntheticMonitor(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/synthetic-monitors/${id}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, `DELETE /api/synthetic-monitors/${id}`);
}
