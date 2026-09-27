// Client for Flare.Api's trace funnel endpoints (src/Flare.Api/Endpoints/TraceFunnelEndpoints.cs):
// per-step figures (`POST /api/traces/funnel`) and one step's traces
// (`POST /api/traces/funnel/traces`). See TraceFunnelQueryBuilder.cs and
// docs-internal/adr/0067-trace-funnels.md for the matching and ordering semantics.
//
// MemoryPack over the wire, same shape as `messaging-api.ts`: the two row types are
// generated; the requests, responses and `TraceFunnelStep` carry `IReadOnlyList` members, so
// those are hand-written under `$lib/memorypack/`.

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { spanAttributeBagFromString, spanAttributeFilterOperatorFromString, traceFunnelOutcomeFromString, type TraceFunnelOutcomeName } from '$lib/memorypack/enums';
import { SpanAttributeFilter as GeneratedSpanAttributeFilter } from '$lib/memorypack/SpanAttributeFilter';
import { TraceFunnelStep as GeneratedStep } from '$lib/memorypack/TraceFunnelStep';
import { TraceFunnelRequest as GeneratedRequest } from '$lib/memorypack/TraceFunnelRequest';
import { TraceFunnelResponse as GeneratedResponse } from '$lib/memorypack/TraceFunnelResponse';
import { TraceFunnelTracesRequest as GeneratedTracesRequest } from '$lib/memorypack/TraceFunnelTracesRequest';
import { TraceFunnelTracesResponse as GeneratedTracesResponse } from '$lib/memorypack/TraceFunnelTracesResponse';
import type { SpanAttributeFilter } from './traces-api';

/** Mirrors `TraceFunnelQueryBuilder.MinSteps`/`MaxSteps`. */
export const MIN_FUNNEL_STEPS = 2;
export const MAX_FUNNEL_STEPS = 6;

/** One step - every set condition must hold; at least one must be set. `''` = not set. */
export interface TraceFunnelStep {
	serviceName: string;
	spanName: string;
	attributes: SpanAttributeFilter[];
}

/** Cumulative: a trace counts only if it reached every earlier step in order. Transition latencies are into this step from the previous one, milliseconds, 0 for the first step. */
export interface TraceFunnelStepResult {
	traceCount: number;
	errorCount: number;
	avgTransitionMs: number;
	p50TransitionMs: number;
	p95TransitionMs: number;
	p99TransitionMs: number;
}

export interface TraceFunnelResponse {
	windowMinutes: number;
	steps: TraceFunnelStepResult[];
}

export type TraceFunnelOutcome = TraceFunnelOutcomeName;

export interface TraceFunnelTrace {
	traceId: string;
	startUnixMs: number;
	/** 1-based: 1 = entered the funnel only. */
	reachedSteps: number;
	elapsedMs: number;
}

/** A step has something to match on - mirrors `TraceFunnelQueryBuilder.ValidateSteps`. */
export function isStepDefined(step: TraceFunnelStep): boolean {
	return step.serviceName !== '' || step.spanName !== '' || step.attributes.length > 0;
}

function toGeneratedSteps(steps: TraceFunnelStep[]): GeneratedStep[] {
	return steps.map((step) => {
		const dto = new GeneratedStep();
		dto.serviceName = step.serviceName || null;
		dto.spanName = step.spanName || null;
		dto.attributes = step.attributes.map((a) => {
			const attr = new GeneratedSpanAttributeFilter();
			attr.bag = spanAttributeBagFromString(a.bag);
			attr.key = a.key;
			attr.value = a.value;
			attr.operator = spanAttributeFilterOperatorFromString(a.operator ?? 'Equals');
			attr.values = a.values ?? null;
			return attr;
		});
		return dto;
	});
}

/** Flare.Api answers a bad step list with a 400 problem - surface its `detail` rather than a bare status. */
async function failure(res: Response, route: string): Promise<Error> {
	try {
		const problem = (await res.json()) as { detail?: string };
		if (problem.detail) return new Error(problem.detail);
	} catch {
		// Not a problem+json body - fall through to the status line.
	}
	return new Error(`POST ${route} failed: ${res.status} ${res.statusText}`);
}

export async function getTraceFunnel(steps: TraceFunnelStep[], windowMinutes: number, signal?: AbortSignal): Promise<TraceFunnelResponse> {
	const request = new GeneratedRequest();
	request.windowMinutes = windowMinutes;
	request.steps = toGeneratedSteps(steps);

	const route = '/api/traces/funnel';
	const res = await apiFetch(`${API_BASE_URL}${route}`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedRequest.serialize(request)),
		signal
	});
	if (!res.ok) throw await failure(res, route);
	const dto = GeneratedResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding TraceFunnelResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		steps: (dto.steps ?? [])
			.filter((s) => s != null)
			.map((s) => ({
				traceCount: Number(s.traceCount),
				errorCount: Number(s.errorCount),
				avgTransitionMs: s.avgTransitionMs,
				p50TransitionMs: s.p50TransitionMs,
				p95TransitionMs: s.p95TransitionMs,
				p99TransitionMs: s.p99TransitionMs
			}))
	};
}

export async function getTraceFunnelTraces(
	steps: TraceFunnelStep[],
	windowMinutes: number,
	stepIndex: number,
	outcome: TraceFunnelOutcome,
	signal?: AbortSignal
): Promise<TraceFunnelTrace[]> {
	const request = new GeneratedTracesRequest();
	request.windowMinutes = windowMinutes;
	request.steps = toGeneratedSteps(steps);
	request.stepIndex = stepIndex;
	request.outcome = traceFunnelOutcomeFromString(outcome);

	const route = '/api/traces/funnel/traces';
	const res = await apiFetch(`${API_BASE_URL}${route}`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedTracesRequest.serialize(request)),
		signal
	});
	if (!res.ok) throw await failure(res, route);
	const dto = GeneratedTracesResponse.deserialize(await res.arrayBuffer());
	return (dto?.traces ?? [])
		.filter((t) => t != null)
		.map((t) => ({
			traceId: t.traceId ?? '',
			startUnixMs: Number(t.startUnixMs),
			reachedSteps: t.reachedSteps,
			elapsedMs: t.elapsedMs
		}));
}
