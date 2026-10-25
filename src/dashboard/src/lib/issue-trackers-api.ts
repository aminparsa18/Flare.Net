// Client for Flare.Api's per-service issue tracker config (`/api/issue-trackers`, ADR-0274) behind
// the /errors "New Jira issue" link. Plain JSON, same shape as `source-links-api.ts`. PUT/DELETE are
// Admin-only server-side.

import { API_BASE_URL, apiFetch } from './api';
import type { IssueTrackerConfig } from '#lib/errors/issue-links.ts';

const BASE = `${API_BASE_URL}/api/issue-trackers`;

async function failure(res: Response, what: string): Promise<Error> {
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// Not a problem-details body - fall back to the status line.
	}
	return new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

export async function listIssueTrackers(signal?: AbortSignal): Promise<IssueTrackerConfig[]> {
	const res = await apiFetch(BASE, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/issue-trackers');
	return ((await res.json()) as { trackers: IssueTrackerConfig[] }).trackers;
}

export async function setIssueTracker(config: IssueTrackerConfig): Promise<void> {
	const res = await apiFetch(`${BASE}/${encodeURIComponent(config.serviceName)}`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(config)
	});
	if (!res.ok) throw await failure(res, 'PUT /api/issue-trackers');
}

export async function deleteIssueTracker(serviceName: string): Promise<void> {
	const res = await apiFetch(`${BASE}/${encodeURIComponent(serviceName)}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, 'DELETE /api/issue-trackers');
}
