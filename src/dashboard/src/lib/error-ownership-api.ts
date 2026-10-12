// Client for Flare.Api's ownership-rules API (`/api/errors/ownership-rules`, ADR-0195): the whole
// ordered rule list is read and replaced as one document. Plain JSON, like `error-issues-api.ts`.

import { API_BASE_URL, apiFetch } from './api';
import type { OwnershipRule } from '$lib/errors/ownership';

const URL = `${API_BASE_URL}/api/errors/ownership-rules`;

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

export async function listOwnershipRules(signal?: AbortSignal): Promise<OwnershipRule[]> {
	const res = await apiFetch(URL, { headers: { Accept: 'application/json' }, signal });
	await expectOk(res, 'GET /api/errors/ownership-rules');
	return ((await res.json()) as { rules: OwnershipRule[] }).rules;
}

export async function replaceOwnershipRules(rules: OwnershipRule[]): Promise<OwnershipRule[]> {
	const res = await apiFetch(URL, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
		body: JSON.stringify({ rules })
	});
	await expectOk(res, 'PUT /api/errors/ownership-rules');
	return ((await res.json()) as { rules: OwnershipRule[] }).rules;
}
