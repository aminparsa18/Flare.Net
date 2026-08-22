// Client for Flare.Api's ingest API-key management (src/Flare.Api/Endpoints/
// IngestApiKeyEndpoints.cs, admin-only). Field names/casing are a hand-mirror of
// src/Flare.Api/Model/IngestApiKeyModels.cs (camelCase properties), same convention
// alerts-api.ts documents for AlertModels.cs. Keep in sync with that file by hand.
//
// Only create() is wrapped here, matching Flare.Cli's own deliberately-scoped
// ApiKeyCreateCommand - GET /api/ingest-keys and DELETE /api/ingest-keys/{id} exist on
// the backend but aren't exposed from either front end yet.

import { API_BASE_URL, apiFetch } from './api';

export interface IngestApiKeyDto {
	id: string;
	name: string;
	createdAt: string;
	revokedAt: string | null;
	isActive: boolean;
}

export interface CreateIngestApiKeyRequest {
	name: string;
}

/** `rawKey` is shown exactly once, here - Flare never stores or displays it again after this response. */
export interface CreateIngestApiKeyResponse {
	key: IngestApiKeyDto;
	rawKey: string;
}

export async function createIngestApiKey(request: CreateIngestApiKeyRequest): Promise<CreateIngestApiKeyResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/ingest-keys`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`POST /api/ingest-keys failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}
