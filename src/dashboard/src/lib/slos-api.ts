// Client for Flare.Api's SLO API (`/api/slos`, see src/Flare.Api/Endpoints/SloEndpoints.cs).
// Plain JSON, like `version-comparison-api.ts` - small flat records, no MemoryPack adapter to
// maintain. See `docs-internal/adr/0108-slo-error-budgets.md`.

import { API_BASE_URL, apiFetch } from './api';

export type SloKind = 'Availability' | 'Latency';

/** Thresholds a latency SLO can use - `Flare.Api.Slos.SloLatencyLadder` (`span_sli_minute` has one counter per rung). */
export const SLO_LATENCY_LADDER_MS = [50, 100, 250, 500, 1000, 2500, 5000, 10000] as const;

export interface Slo {
	id: string;
	name: string;
	description: string;
	kind: SloKind;
	serviceName: string;
	/** Entry-span name (endpoint); empty = every entry span of the service. */
	operationName: string;
	/** Percent of events that must be good, e.g. 99.5. */
	targetPercent: number;
	/** Latency SLOs only (a ladder rung); 0 for availability. */
	latencyThresholdMs: number;
	windowDays: number;
	createdAt: string;
	updatedAt: string;
	/** Owning project (ADR-0123); `null` = instance-wide. */
	projectId: string | null;
}

export interface SloRequest {
	name: string;
	description: string;
	kind: SloKind;
	serviceName: string;
	operationName: string;
	targetPercent: number;
	latencyThresholdMs: number | null;
	windowDays: number;
	/** Omitted keeps an update's current project; `NO_PROJECT` clears it (ADR-0123). */
	projectId?: string | null;
}

export interface SloWindowStats {
	windowSeconds: number;
	total: number;
	bad: number;
	/** Budget spend rate over this window (1 = exactly the budget); null with no events. */
	burnRate: number | null;
}

export interface SloSeriesPoint {
	timeUnixMs: number;
	total: number;
	bad: number;
}

export interface SloStatus {
	slo: Slo;
	total: number;
	bad: number;
	/** Percent of good events over the window; null with no events. */
	sli: number | null;
	/** 1 = budget untouched, 0 = spent, negative = overspent; null with no events. */
	errorBudgetRemaining: number | null;
	burnRates: SloWindowStats[];
	series: SloSeriesPoint[];
}

async function expectOk(res: Response, what: string): Promise<void> {
	if (res.ok) return;
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// not a problem+json body
	}
	throw new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

const jsonHeaders = { 'Content-Type': 'application/json' };

export async function listSlos(signal?: AbortSignal): Promise<Slo[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/slos`, { signal });
	await expectOk(res, 'GET /api/slos');
	return ((await res.json()) as { slos: Slo[] }).slos;
}

export async function getSloStatus(id: string, signal?: AbortSignal): Promise<SloStatus> {
	const res = await apiFetch(`${API_BASE_URL}/api/slos/${id}/status`, { signal });
	await expectOk(res, 'GET /api/slos/{id}/status');
	return (await res.json()) as SloStatus;
}

export async function createSlo(request: SloRequest): Promise<Slo> {
	const res = await apiFetch(`${API_BASE_URL}/api/slos`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	await expectOk(res, 'POST /api/slos');
	return (await res.json()) as Slo;
}

export async function updateSlo(id: string, request: SloRequest): Promise<Slo> {
	const res = await apiFetch(`${API_BASE_URL}/api/slos/${id}`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(request) });
	await expectOk(res, 'PUT /api/slos/{id}');
	return (await res.json()) as Slo;
}

export async function deleteSlo(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/slos/${id}`, { method: 'DELETE' });
	await expectOk(res, 'DELETE /api/slos/{id}');
}
