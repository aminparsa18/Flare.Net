// Client for Flare.Api's opt-in AI actions (`/api/ai/*`, ADR-0103). Plain JSON, not MemoryPack.

import { API_BASE_URL, apiFetch } from './api';

const BASE = `${API_BASE_URL}/api/ai`;

export interface ExplainExceptionRequest {
	serviceName: string;
	exceptionType: string;
	exceptionMessage?: string;
	stacktrace?: string;
	/** Throw-site frame; the API fetches the source itself and redacts it before it goes to the model. */
	source?: { serviceName: string; ref: string; isCommit: boolean; path: string; line: number };
}

export interface ExplainExceptionResponse {
	explanation: string;
	model: string;
	includedSource: boolean;
}

/** Whether an admin configured a model. A failed call counts as disabled so the action stays hidden. */
export async function getAiEnabled(signal?: AbortSignal): Promise<boolean> {
	try {
		const res = await apiFetch(`${BASE}/status`, { signal });
		return res.ok && ((await res.json()) as { enabled: boolean }).enabled;
	} catch {
		return false;
	}
}

export async function explainException(request: ExplainExceptionRequest, signal?: AbortSignal): Promise<ExplainExceptionResponse> {
	const res = await apiFetch(`${BASE}/explain-exception`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) {
		let detail = '';
		try {
			detail = ((await res.json()) as { detail?: string }).detail ?? '';
		} catch {
			// Not a problem-details body.
		}
		throw new Error(detail || `POST /api/ai/explain-exception failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as ExplainExceptionResponse;
}
