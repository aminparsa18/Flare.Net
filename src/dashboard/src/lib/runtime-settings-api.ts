// Client for Flare.Api's runtime settings (`/api/settings/pii-redaction`, ADR-0210).
//
// Plain JSON, not MemoryPack - one tiny row, same call `telemetry-export-api.ts` makes.

import { API_BASE_URL, apiFetch } from './api';

/** `enabled` is undefined when no override is saved: Flare.Ingest then follows its own `PipelineRules__Pii__Enabled`. */
export interface PiiRedactionSetting {
	enabled?: boolean | null;
	updatedAt?: string | null;
	updatedBy?: string;
}

async function failure(res: Response, what: string): Promise<Error> {
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// Not a problem-details body - fall back to the status line.
	}
	return new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

export async function getPiiRedaction(signal?: AbortSignal): Promise<PiiRedactionSetting> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/pii-redaction`, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/settings/pii-redaction');
	return (await res.json()) as PiiRedactionSetting;
}

export async function setPiiRedaction(enabled: boolean): Promise<PiiRedactionSetting> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/pii-redaction`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ enabled })
	});
	if (!res.ok) throw await failure(res, 'Saving the PII redaction setting');
	return (await res.json()) as PiiRedactionSetting;
}

/** Drops the override so Flare.Ingest follows its configuration again. */
export async function clearPiiRedaction(): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/settings/pii-redaction`, { method: 'DELETE' });
	if (!res.ok && res.status !== 404) throw await failure(res, 'Resetting the PII redaction setting');
}
