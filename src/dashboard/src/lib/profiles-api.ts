// Client for Flare.Api's continuous-profiling endpoints (`POST /api/profiles/types` and
// `POST /api/profiles/flamegraph`, src/Flare.Api/Endpoints/ProfileEndpoints.cs, ADR-0141).
// Plain JSON, same as `n-plus-one-api.ts`: the flame graph is a recursive tree, which the
// MemoryPack adapter layer isn't worth maintaining for.

import { API_BASE_URL, apiFetch } from './api';

export interface ProfileTypeInfo {
	service: string;
	sampleType: string;
	sampleUnit: string;
	sampleCount: number;
	lastSeenUnixMs: number;
}

export interface ProfileTypesResponse {
	windowMinutes: number;
	types: ProfileTypeInfo[];
}

export interface FlameGraphNode {
	name: string;
	/** Summed value for this frame and everything below it. */
	total: number;
	/** The part of `total` spent in this frame itself. */
	self: number;
	children: FlameGraphNode[];
}

export interface FlameGraphResponse {
	windowMinutes: number;
	sampleUnit: string;
	stackCount: number;
	/** The stack cap was hit, so the lightest stacks are missing. */
	truncated: boolean;
	/** Synthetic `all` root; its children are the real root frames. */
	root: FlameGraphNode;
}

export interface FlameGraphRequest {
	service: string;
	sampleType: string;
	windowMinutes?: number;
	endUnixMs?: number;
	traceId?: string;
	spanId?: string;
}

async function post<T>(path: string, body: unknown, what: string, signal?: AbortSignal): Promise<T> {
	const res = await apiFetch(`${API_BASE_URL}${path}`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(body),
		signal
	});
	if (!res.ok) throw new Error(`${what} failed: ${res.status} ${res.statusText}`);
	return (await res.json()) as T;
}

export function getProfileTypes(windowMinutes: number, signal?: AbortSignal): Promise<ProfileTypesResponse> {
	return post('/api/profiles/types', { windowMinutes }, 'Profile types query', signal);
}

export function getFlameGraph(request: FlameGraphRequest, signal?: AbortSignal): Promise<FlameGraphResponse> {
	return post('/api/profiles/flamegraph', request, 'Flame graph query', signal);
}
