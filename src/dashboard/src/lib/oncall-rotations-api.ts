// Client for Flare.Api's on-call rotation API (`/api/oncall-rotations` CRUD). JSON over the wire
// (these responses are small and aren't MemoryPack'd). See
// `docs-internal/adr/0126-alert-oncall-rotations.md`.

import { API_BASE_URL, apiFetch } from './api';

/** A one-off swap: `channelId` is on call from `startsAt` to `endsAt` (ISO instants). */
export interface OnCallOverride {
	startsAt: string;
	endsAt: string;
	channelId: string;
}

/** Weekly window a rotation pages in: `days` are 0 (Sunday) to 6; minutes are after local midnight in `timeZone`. */
export interface OnCallCoverage {
	timeZone: string;
	days: number[];
	startMinute: number;
	endMinute: number;
}

export interface OnCallRotation {
	id: string;
	name: string;
	description: string;
	/** Participants (notification channel ids) in shift order. */
	channelIds: string[];
	shiftHours: number;
	/** ISO instant the first shift starts. */
	startsAt: string;
	overrides: OnCallOverride[];
	coverage?: OnCallCoverage | null;
	createdAt: string;
	updatedAt: string;
}

/** A rotation plus who is on call right now (computed server-side). */
export interface OnCallRotationStatus {
	rotation: OnCallRotation;
	onCallChannelId: string;
	shiftEndsAt: string;
	nextChannelId: string;
	/** True when `onCallChannelId` is a one-off override rather than the scheduled participant. */
	isOverride?: boolean;
	/** False when the rotation's coverage window is closed, so nobody is paged. */
	inCoverage?: boolean;
}

export interface OnCallRotationRequest {
	name: string;
	description: string;
	channelIds: string[];
	shiftHours: number;
	startsAt: string;
	overrides: OnCallOverride[];
	coverage: OnCallCoverage | null;
}

async function failure(res: Response, what: string): Promise<Error> {
	let message = `${what} failed: ${res.status} ${res.statusText}`;
	try {
		const problem = await res.json();
		message = problem?.detail || problem?.title || message;
	} catch {
		// Not JSON - keep the generic message.
	}
	return new Error(message);
}

const jsonHeaders = { 'Content-Type': 'application/json', Accept: 'application/json' };

export async function listOnCallRotations(signal?: AbortSignal): Promise<OnCallRotationStatus[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/oncall-rotations`, { headers: { Accept: 'application/json' }, signal });
	if (!res.ok) throw await failure(res, 'GET /api/oncall-rotations');
	return ((await res.json()) as { rotations?: OnCallRotationStatus[] }).rotations ?? [];
}

export async function createOnCallRotation(request: OnCallRotationRequest): Promise<OnCallRotationStatus> {
	const res = await apiFetch(`${API_BASE_URL}/api/oncall-rotations`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, 'POST /api/oncall-rotations');
	return (await res.json()) as OnCallRotationStatus;
}

export async function updateOnCallRotation(id: string, request: OnCallRotationRequest): Promise<OnCallRotationStatus> {
	const res = await apiFetch(`${API_BASE_URL}/api/oncall-rotations/${id}`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(request) });
	if (!res.ok) throw await failure(res, `PUT /api/oncall-rotations/${id}`);
	return (await res.json()) as OnCallRotationStatus;
}

/** 204 No Content on success. */
export async function deleteOnCallRotation(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/oncall-rotations/${id}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, `DELETE /api/oncall-rotations/${id}`);
}
