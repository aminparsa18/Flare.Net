// Client for Flare.Api's error-issue API (`/api/errors/issues`, see
// src/Flare.Api/Endpoints/ErrorIssueEndpoints.cs) - per-exception-group triage state for the
// /errors page. Plain JSON, like `slos-api.ts` - small flat records, no MemoryPack adapter to
// maintain. See `docs-internal/adr/0121-error-issue-lifecycle.md`.

import { API_BASE_URL, apiFetch } from './api';

/** `Regressed` is only ever returned (derived server-side), never sent. */
export type ErrorIssueStatus = 'Open' | 'Resolved' | 'Ignored' | 'Regressed';

export interface ErrorIssue {
	id: string;
	exceptionType: string;
	exceptionMessage: string;
	/** Effective status: a lapsed ignore reads Open, a resolved group recurring in a new version reads Regressed. */
	status: ErrorIssueStatus;
	/** Empty = unassigned. */
	assignee: string;
	statusChangedAt: string;
	statusChangedBy: string;
	ignoreUntil: string | null;
	ignoreUntilOccurrences: number | null;
	/** Ignored with an occurrence limit: occurrences since the ignore began. */
	occurrencesSinceChange: number;
	knownVersions: string[];
	/** Regressed only: the version it recurred in ("" when the app reports none). */
	regressedVersion: string;
}

export interface ErrorIssueRequest {
	exceptionType: string;
	exceptionMessage: string;
	/** Omit to keep the current status. */
	status?: Exclude<ErrorIssueStatus, 'Regressed'>;
	ignoreUntil?: string;
	ignoreUntilOccurrences?: number;
	/** '' clears the assignment; omit to keep it. */
	assignee?: string;
}

/** Key a group's issue is looked up by in the page - the pair, since the fingerprint is server-side. */
export function issueKey(exceptionType: string, exceptionMessage: string): string {
	return `${exceptionType}\u001f${exceptionMessage}`;
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

export async function listErrorIssues(signal?: AbortSignal): Promise<ErrorIssue[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/errors/issues`, { headers: { Accept: 'application/json' }, signal });
	await expectOk(res, 'GET /api/errors/issues');
	return ((await res.json()) as { issues: ErrorIssue[] }).issues;
}

/** Returns the group's new state, or null when it went back to Open and unassigned (no stored state). */
export async function upsertErrorIssue(request: ErrorIssueRequest): Promise<ErrorIssue | null> {
	const res = await apiFetch(`${API_BASE_URL}/api/errors/issues`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
		body: JSON.stringify(request)
	});
	await expectOk(res, 'PUT /api/errors/issues');
	return res.status === 204 ? null : ((await res.json()) as ErrorIssue);
}
