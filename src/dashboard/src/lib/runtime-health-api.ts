// Client for Flare.Api's .NET runtime health findings (`POST /api/services/runtime-health`, see
// src/Flare.Api/Endpoints/ServicesEndpoints.cs and RuntimeHealthDetector.cs). Plain JSON - a
// short flat list of findings, so there's no MemoryPack adapter to maintain (same as
// `n-plus-one-api.ts`).

import { API_BASE_URL, apiFetch } from './api';

export type RuntimeFindingKind = 'ThreadPoolStarvation' | 'GcPressure' | 'LockContention' | 'ExceptionRate';
export type RuntimeFindingSeverity = 'Warning' | 'Critical';

export interface RuntimeHealthFinding {
	kind: RuntimeFindingKind;
	severity: RuntimeFindingSeverity;
	/** `service.instance.id`, else pod name, else `host.name`; empty when the service reports none. */
	instance: string;
	startUnixMs: number;
	endUnixMs: number;
	/** Reaches the end of the window, so it may still be happening. */
	ongoing: boolean;
	/** Peak queue length / fraction of time in GC (0-1) / events per second, depending on `kind`. */
	value: number;
	/** Typical completed work items per second (starvation) or events per second (the rate kinds); null for GC. */
	baseline: number | null;
}

export interface RuntimeHealthResponse {
	windowMinutes: number;
	/** False means the service sent no `dotnet.*` metrics - "no data", not "healthy". */
	hasRuntimeMetrics: boolean;
	findings: RuntimeHealthFinding[];
}

export async function getRuntimeHealth(service: string, windowMinutes: number, signal?: AbortSignal): Promise<RuntimeHealthResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/services/runtime-health`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ service, windowMinutes }),
		signal
	});
	if (!res.ok) throw new Error(`Runtime health query failed: ${res.status} ${res.statusText}`);
	return (await res.json()) as RuntimeHealthResponse;
}
