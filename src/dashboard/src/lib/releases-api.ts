// Client for Flare.Api's release markers (`/api/releases`, see src/Flare.Api/Endpoints/ReleaseEndpoints.cs).
// Plain JSON, like `error-issues-api.ts` - small flat records, no MemoryPack adapter to maintain.
// See `docs-internal/adr/0182-release-tracking.md`.

import { API_BASE_URL, apiFetch } from './api';

export interface Release {
	id: string;
	service: string;
	version: string;
	commit: string;
	url: string;
	notes: string;
	deployedAt: string;
	createdBy: string;
	/** Exception groups first seen under this version; null unless the list was scoped to one service. */
	newErrorCount: number | null;
}

export interface ReleaseNewError {
	exceptionType: string;
	exceptionMessage: string;
	occurrences: number;
	firstSeenUnixMs: number;
	lastSeenUnixMs: number;
}

export interface ReleaseErrors {
	errors: ReleaseNewError[];
	/** How far before the deploy earlier occurrences were looked for. */
	historyDays: number;
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

export async function listReleases(service?: string, signal?: AbortSignal): Promise<Release[]> {
	const query = service ? `?service=${encodeURIComponent(service)}` : '';
	const res = await apiFetch(`${API_BASE_URL}/api/releases${query}`, { headers: { Accept: 'application/json' }, signal });
	await expectOk(res, 'GET /api/releases');
	return ((await res.json()) as { releases: Release[] }).releases;
}

export async function getReleaseErrors(service: string, version: string, signal?: AbortSignal): Promise<ReleaseErrors> {
	const query = `?service=${encodeURIComponent(service)}&version=${encodeURIComponent(version)}`;
	const res = await apiFetch(`${API_BASE_URL}/api/releases/errors${query}`, { headers: { Accept: 'application/json' }, signal });
	await expectOk(res, 'GET /api/releases/errors');
	return (await res.json()) as ReleaseErrors;
}

export async function deleteRelease(service: string, version: string): Promise<void> {
	const query = `?service=${encodeURIComponent(service)}&version=${encodeURIComponent(version)}`;
	const res = await apiFetch(`${API_BASE_URL}/api/releases${query}`, { method: 'DELETE' });
	await expectOk(res, 'DELETE /api/releases');
}
