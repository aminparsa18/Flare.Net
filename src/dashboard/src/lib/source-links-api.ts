// Client for Flare.Api's per-service source repo config (`/api/source-links`, ADR-0095) behind
// the /errors stack-trace links. Plain JSON, not MemoryPack - a handful of small config rows,
// same call as `metric-attribute-rules-api.ts`. PUT/DELETE are Admin-only server-side.

import { API_BASE_URL, apiFetch } from './api';
import type { SourceLinkConfig } from '#lib/errors/source-links.ts';

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

export interface SourceSnippet {
	/** 1-based number of `lines[0]`. */
	startLine: number;
	lines: string[];
}

/** Lines around a frame, fetched by the API from the service's repo host. Throws with the API's reason (e.g. private repo, no token). */
export async function getSourceSnippet(
	request: { serviceName: string; ref: string; isCommit: boolean; path: string; line: number },
	signal?: AbortSignal
): Promise<SourceSnippet> {
	const res = await apiFetch(`${BASE}/snippet`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) throw await failure(res, 'POST /api/source-links/snippet');
	return (await res.json()) as SourceSnippet;
}

export interface SuspectCommit {
	sha: string;
	/** First line of the commit message. */
	message: string;
	author: string;
	date: string | null;
	url: string;
	/** Which of the requested paths this commit changed, throw site first. */
	matchedPaths: string[];
}

export interface SuspectCommits {
	/** Best candidate first; empty when no commit in the range touched a path. */
	commits: SuspectCommit[];
	totalCommits: number;
	/** The range was longer than what was inspected, so a suspect may be missing. */
	truncated: boolean;
	compareUrl: string;
}

/** Commits between two refs of the service's repo that changed one of `paths` (ADR-0263). Throws with the API's reason. */
export async function getSuspectCommits(
	request: { serviceName: string; baseRef: string; headRef: string; paths: string[] },
	signal?: AbortSignal
): Promise<SuspectCommits> {
	const res = await apiFetch(`${BASE}/suspects`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) throw await failure(res, 'POST /api/source-links/suspects');
	return (await res.json()) as SuspectCommits;
}
