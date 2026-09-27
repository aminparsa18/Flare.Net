// Client for Flare.Api's `GET /api/version` (src/Flare.Api/Endpoints/VersionEndpoints.cs):
// the running version plus the latest Flare release on GitHub, for the "new version
// available" notice (ADR-0068).
//
// Plain JSON, not MemoryPack - fetched once per page load, and the response carries a
// `DateTimeOffset`, which would need a hand-written decoder for no measurable gain.

import { API_BASE_URL, apiFetch } from './api';

export interface LatestReleaseInfo {
	/** Normalized, no `v` prefix - e.g. `0.6.0`. */
	version: string;
	tag: string;
	/** The release page on GitHub. */
	url: string;
	/** Null when only a git tag exists (no published GitHub Release). */
	name: string | null;
	publishedAt: string | null;
	/** Release body (Markdown source, truncated server-side); null when only a tag exists. */
	notes: string | null;
}

export interface VersionInfo {
	/** This build's version, or `dev` for a non-release build. */
	current: string;
	updateAvailable: boolean;
	/** Null when the check is off (`UpdateCheck__Enabled=false`), skipped for a dev build, or hasn't succeeded. */
	latest: LatestReleaseInfo | null;
}

export async function getVersionInfo(signal?: AbortSignal): Promise<VersionInfo> {
	const res = await apiFetch(`${API_BASE_URL}/api/version`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/version failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as VersionInfo;
}
