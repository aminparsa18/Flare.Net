// Client for Flare.Api's per-service source repo config (`/api/source-links`, ADR-0095) behind
// the /errors stack-trace links. Plain JSON, not MemoryPack - a handful of small config rows,
// same call as `metric-attribute-rules-api.ts`. PUT/DELETE are Admin-only server-side.

import { API_BASE_URL, apiFetch } from './api';
import type { SourceLinkConfig } from '$lib/errors/source-links';

const BASE = `${API_BASE_URL}/api/source-links`;

async function failure(res: Response, what: string): Promise<Error> {
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// Not a problem-details body - fall back to the status line.
	}
	return new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

export async function listSourceLinks(signal?: AbortSignal): Promise<SourceLinkConfig[]> {
	const res = await apiFetch(BASE, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/source-links');
	return ((await res.json()) as { links: SourceLinkConfig[] }).links;
}

export async function setSourceLink(config: SourceLinkConfig): Promise<void> {
	const res = await apiFetch(`${BASE}/${encodeURIComponent(config.serviceName)}`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(config)
	});
	if (!res.ok) throw await failure(res, 'PUT /api/source-links');
}

export async function deleteSourceLink(serviceName: string): Promise<void> {
	const res = await apiFetch(`${BASE}/${encodeURIComponent(serviceName)}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, 'DELETE /api/source-links');
}
