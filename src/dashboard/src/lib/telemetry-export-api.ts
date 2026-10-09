// Client for Flare.Api's managed telemetry export (`/api/forwarding/*`, `/api/archive/*`, ADR-0157).
//
// Plain JSON, not MemoryPack - a handful of small config rows, same call `retention-api.ts` makes.
// Credentials (header values, S3 keys) come back masked; sending a masked value back unchanged keeps
// the stored one, so an edit form can round-trip without ever holding the secret.

import { API_BASE_URL, apiFetch } from './api';

export type TelemetrySignal = 'Logs' | 'Traces' | 'Metrics';
export const TELEMETRY_SIGNALS: TelemetrySignal[] = ['Logs', 'Traces', 'Metrics'];
export type ArchiveFileFormat = 'Parquet' | 'Ndjson';

export interface ForwardingTarget {
	id: string;
	name: string;
	enabled: boolean;
	endpoint: string;
	headers: Record<string, string>;
	signals: TelemetrySignal[];
	services: string[];
	ingestKeyIds: string[];
	gzip: boolean;
	createdAt: string;
	updatedAt: string;
}

export interface ForwardingTargetRequest {
	name: string;
	enabled: boolean;
	endpoint: string;
	headers: Record<string, string>;
	signals: TelemetrySignal[];
	services: string[];
	ingestKeyIds: string[];
	gzip: boolean;
}

export interface ForwardingTargetStatus {
	key: string;
	name: string;
	source: 'managed' | 'config';
	pending: number;
	sent: number;
	failed: number;
	lastSuccessAt: string | null;
	lastError: string | null;
	lastErrorAt: string | null;
}

export interface ArchiveSettings {
	enabled: boolean;
	endpoint: string;
	accessKey: string;
	secretKey: string;
	prefix: string;
	format: ArchiveFileFormat;
	signals: TelemetrySignal[];
	saved: boolean;
	updatedAt: string | null;
}

export interface ArchiveSettingsRequest {
	enabled: boolean;
	endpoint: string;
	accessKey: string;
	secretKey: string;
	prefix: string;
	format: ArchiveFileFormat;
	signals: TelemetrySignal[];
}

export interface ArchiveTableStatus {
	table: string;
	lastExportedHour: string | null;
	lastSuccessAt: string | null;
	lastRows: number;
	lastError: string | null;
	lastErrorAt: string | null;
}

export interface ArchiveStatus {
	active: boolean;
	source: string;
	checkedAt: string | null;
	tables: ArchiveTableStatus[];
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

const JSON_HEADERS = { 'Content-Type': 'application/json' };

export async function listForwardingTargets(signal?: AbortSignal): Promise<ForwardingTarget[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/forwarding/targets`, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/forwarding/targets');
	return ((await res.json()) as { targets: ForwardingTarget[] }).targets;
}

export async function saveForwardingTarget(id: string | null, request: ForwardingTargetRequest): Promise<ForwardingTarget> {
	const res = await apiFetch(`${API_BASE_URL}/api/forwarding/targets${id ? `/${id}` : ''}`, {
		method: id ? 'PUT' : 'POST',
		headers: JSON_HEADERS,
		body: JSON.stringify(request)
	});
	if (!res.ok) throw await failure(res, 'Saving the forwarding target');
	return (await res.json()) as ForwardingTarget;
}

export async function deleteForwardingTarget(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/forwarding/targets/${id}`, { method: 'DELETE' });
	if (!res.ok && res.status !== 404) throw await failure(res, 'Deleting the forwarding target');
}

export async function getForwardingStatus(signal?: AbortSignal): Promise<ForwardingTargetStatus[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/forwarding/status`, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/forwarding/status');
	return ((await res.json()) as { targets: ForwardingTargetStatus[] }).targets;
}

export async function getArchiveSettings(signal?: AbortSignal): Promise<ArchiveSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/archive/settings`, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/archive/settings');
	return (await res.json()) as ArchiveSettings;
}

export async function saveArchiveSettings(request: ArchiveSettingsRequest): Promise<ArchiveSettings> {
	const res = await apiFetch(`${API_BASE_URL}/api/archive/settings`, {
		method: 'PUT',
		headers: JSON_HEADERS,
		body: JSON.stringify(request)
	});
	if (!res.ok) throw await failure(res, 'Saving the archive settings');
	return (await res.json()) as ArchiveSettings;
}

/** Drops the saved settings so the archive follows the worker's configuration again. */
export async function resetArchiveSettings(): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/archive/settings`, { method: 'DELETE' });
	if (!res.ok && res.status !== 404) throw await failure(res, 'Resetting the archive settings');
}

export async function getArchiveStatus(signal?: AbortSignal): Promise<ArchiveStatus> {
	const res = await apiFetch(`${API_BASE_URL}/api/archive/status`, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/archive/status');
	return (await res.json()) as ArchiveStatus;
}
