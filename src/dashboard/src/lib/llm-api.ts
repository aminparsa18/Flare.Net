// Client for Flare.Api's /llm page endpoint (src/Flare.Api/Endpoints/LlmEndpoints.cs):
// `POST /api/llm/models`, one row per (provider, model) derived from spans' `gen_ai.*`
// attributes - see LlmQueryBuilder.cs and docs-internal/adr/0100-llm-observability-genai-spans.md.
//
// MemoryPack over the wire, same shape as `external-apis-api.ts`: the request and row type are
// generated classes; the response is hand-written under `$lib/memorypack/`.

import { API_BASE_URL, apiFetch, memoryPackBody, memoryPackRequestHeaders } from './api';
import { LlmModelsRequest as GeneratedModelsRequest } from '$lib/generated/memorypack/LlmModelsRequest.js';
import { SetLlmModelPriceRequest as GeneratedSetPriceRequest } from '$lib/generated/memorypack/SetLlmModelPriceRequest.js';
import { LlmModelsResponse as GeneratedModelsResponse } from '$lib/memorypack/LlmModelsResponse';

/** One model's calls over the window. Latencies are milliseconds. */
export interface LlmModel {
	/** `gen_ai.provider.name` (else `gen_ai.system`); '' when the instrumentation set neither. */
	provider: string;
	model: string;
	callCount: number;
	errorCount: number;
	perSecond: number;
	p50Ms: number;
	p95Ms: number;
	p99Ms: number;
	inputTokens: number;
	outputTokens: number;
	serviceCount: number;
	lastSeenUnixMs: number;
	/** USD per million tokens used for the estimate; null when no price is known. */
	inputPricePerMillion: number | null;
	outputPricePerMillion: number | null;
	/** True for an admin override, false for a built-in default price. */
	priceIsCustom: boolean;
	/** Estimated USD cost over the window; null when no price is known. */
	estimatedCost: number | null;
}

export interface LlmModelsResponse {
	windowMinutes: number;
	models: LlmModel[];
	/** Every service that made a model call in the window - the toolbar picker, unaffected by the service filter. */
	services: string[];
}

export async function getLlmModels(windowMinutes: number, service: string, signal?: AbortSignal): Promise<LlmModelsResponse> {
	const request = new GeneratedModelsRequest();
	request.windowMinutes = windowMinutes;
	request.service = service || null;

	const res = await apiFetch(`${API_BASE_URL}/api/llm/models`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedModelsRequest.serialize(request)),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/llm/models failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedModelsResponse.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding LlmModelsResponse.');
	}
	return {
		windowMinutes: dto.windowMinutes,
		services: (dto.services ?? []).filter((s): s is string => s != null),
		models: (dto.models ?? [])
			.filter((d) => d != null)
			.map((d) => ({
				provider: d.provider ?? '',
				model: d.model ?? '',
				callCount: Number(d.callCount),
				errorCount: Number(d.errorCount),
				perSecond: d.perSecond,
				p50Ms: d.p50Ms,
				p95Ms: d.p95Ms,
				p99Ms: d.p99Ms,
				inputTokens: Number(d.inputTokens),
				outputTokens: Number(d.outputTokens),
				serviceCount: Number(d.serviceCount),
				lastSeenUnixMs: Number(d.lastSeenUnixMs),
				inputPricePerMillion: d.inputPricePerMillion,
				outputPricePerMillion: d.outputPricePerMillion,
				priceIsCustom: d.priceIsCustom,
				estimatedCost: d.estimatedCost
			}))
	};
}

/** `PUT /api/llm/prices` - Admin-only. USD per million tokens for one model name, replacing the built-in default. */
export async function setLlmModelPrice(model: string, inputPerMillion: number, outputPerMillion: number): Promise<void> {
	const request = new GeneratedSetPriceRequest();
	request.model = model;
	request.inputPerMillion = inputPerMillion;
	request.outputPerMillion = outputPerMillion;

	const res = await apiFetch(`${API_BASE_URL}/api/llm/prices`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSetPriceRequest.serialize(request))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/llm/prices failed: ${res.status} ${res.statusText}`);
	}
}

/** `DELETE /api/llm/prices` - Admin-only. Reverts the model to its built-in default price (or none). */
export async function resetLlmModelPrice(model: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/llm/prices?model=${encodeURIComponent(model)}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/llm/prices failed: ${res.status} ${res.statusText}`);
	}
}
