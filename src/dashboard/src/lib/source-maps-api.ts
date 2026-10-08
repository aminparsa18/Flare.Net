// Client for Flare.Api's source-map management (src/Flare.Api/Endpoints/SourceMapEndpoints.cs,
// ADR-0152). Plain JSON (the endpoint's unconditional default): the list is small and uploads
// come from CI (`flare sourcemaps upload`), so there is no MemoryPack type for it. Listing is
// open to any signed-in user; delete is Admin-only.

import { API_BASE_URL, apiFetch } from './api';

export interface SourceMapDto {
	serviceName: string;
	version: string;
	bundle: string;
	/** Uncompressed size. */
	sizeBytes: number;
	uploadedAt: string;
}

export async function listSourceMaps(): Promise<SourceMapDto[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/source-maps`);
	if (!res.ok) {
		throw new Error(`GET /api/source-maps failed: ${res.status} ${res.statusText}`);
	}
	return ((await res.json()) as { maps: SourceMapDto[] }).maps;
}

/** Deletes one bundle's map, or every map of the release when `bundle` is omitted. */
export async function deleteSourceMaps(service: string, version: string, bundle?: string): Promise<void> {
	const params = new URLSearchParams({ service, version });
	if (bundle) params.set('bundle', bundle);
	const res = await apiFetch(`${API_BASE_URL}/api/source-maps?${params}`, { method: 'DELETE' });
	if (!res.ok && res.status !== 404) {
		throw new Error(`DELETE /api/source-maps failed: ${res.status} ${res.statusText}`);
	}
}
