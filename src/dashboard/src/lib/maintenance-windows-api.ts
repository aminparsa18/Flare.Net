// Client for Flare.Api's maintenance-window API (`/api/maintenance-windows` CRUD) - planned
// windows during which alert rules still evaluate but don't notify. See
// `docs-internal/adr/0055-alert-maintenance-windows.md`.
//
// MemoryPack over the wire, same shape as `notification-channels-api.ts`. Every type here is
// hand-written (`$lib/memorypack/MaintenanceWindow*.ts`) - `DateTimeOffset`/`IReadOnlyList<T>`
// members. `recurrence` converts through `$lib/memorypack/enums.ts`.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import {
	maintenanceWindowRecurrenceFromString,
	maintenanceWindowRecurrenceToString,
	type MaintenanceWindowRecurrenceName
} from '$lib/memorypack/enums';
import { MaintenanceWindow as GeneratedMaintenanceWindow } from '$lib/memorypack/MaintenanceWindow';
import { MaintenanceWindowRequest as GeneratedMaintenanceWindowRequest } from '$lib/memorypack/MaintenanceWindowRequest';
import { MaintenanceWindowListResponse as GeneratedMaintenanceWindowListResponse } from '$lib/memorypack/MaintenanceWindowListResponse';

export type MaintenanceWindowRecurrence = MaintenanceWindowRecurrenceName;

export interface MaintenanceWindow {
	id: string;
	name: string;
	description: string;
	/** Alert rules this window silences; empty = every rule. */
	ruleIds: string[];
	/** First (for `'None'`, only) occurrence - a recurring window repeats this span at the same local time of day in `timeZone`. */
	startsAt: string;
	endsAt: string;
	recurrence: MaintenanceWindowRecurrence;
	/** `'Weekly'` only - `Date.getDay()`-style ordinals, 0 = Sunday, in `timeZone`. */
	daysOfWeek: number[];
	/** Recurring only - no occurrence starts at or after this instant; null = forever. */
	repeatUntil: string | null;
	timeZone: string;
	createdAt: string;
	updatedAt: string;
}

export interface MaintenanceWindowRequest {
	name: string;
	description: string;
	ruleIds: string[];
	startsAt: string;
	endsAt: string;
	recurrence: MaintenanceWindowRecurrence;
	daysOfWeek: number[];
	repeatUntil: string | null;
	timeZone: string;
}

export interface MaintenanceWindowListResponse {
	windows: MaintenanceWindow[];
	/** Ids of `windows` active right now (computed server-side, recurrence and time zone included). */
	activeWindowIds: string[];
}

function toMaintenanceWindow(dto: GeneratedMaintenanceWindow): MaintenanceWindow {
	return {
		id: dto.id,
		name: dto.name ?? '',
		description: dto.description ?? '',
		ruleIds: (dto.ruleIds ?? []).filter((id): id is string => id != null),
		startsAt: dto.startsAt.toISOString(),
		endsAt: dto.endsAt.toISOString(),
		recurrence: maintenanceWindowRecurrenceToString(dto.recurrence),
		daysOfWeek: dto.daysOfWeek ?? [],
		repeatUntil: dto.repeatUntil?.toISOString() ?? null,
		timeZone: dto.timeZone ?? 'UTC',
		createdAt: dto.createdAt.toISOString(),
		updatedAt: dto.updatedAt.toISOString()
	};
}

function toGeneratedRequest(request: MaintenanceWindowRequest): GeneratedMaintenanceWindowRequest {
	const dto = new GeneratedMaintenanceWindowRequest();
	dto.name = request.name;
	dto.description = request.description;
	dto.ruleIds = request.ruleIds;
	dto.startsAt = new Date(request.startsAt);
	dto.endsAt = new Date(request.endsAt);
	dto.recurrence = maintenanceWindowRecurrenceFromString(request.recurrence);
	dto.daysOfWeek = request.daysOfWeek;
	dto.repeatUntil = request.repeatUntil == null ? null : new Date(request.repeatUntil);
	dto.timeZone = request.timeZone;
	return dto;
}

/** Validation failures come back as JSON ProblemDetails (see `runLogQlQuery` in `api.ts`) - surface the server's message. */
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

async function decodeMaintenanceWindow(res: Response): Promise<MaintenanceWindow> {
	const dto = GeneratedMaintenanceWindow.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding MaintenanceWindow.');
	}
	return toMaintenanceWindow(dto);
}

export async function listMaintenanceWindows(signal?: AbortSignal): Promise<MaintenanceWindowListResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/maintenance-windows`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw await failure(res, 'GET /api/maintenance-windows');
	}
	const dto = GeneratedMaintenanceWindowListResponse.deserialize(await res.arrayBuffer());
	return {
		windows: (dto?.windows ?? []).filter((w): w is GeneratedMaintenanceWindow => w != null).map(toMaintenanceWindow),
		activeWindowIds: (dto?.activeWindowIds ?? []).filter((id): id is string => id != null)
	};
}

export async function createMaintenanceWindow(request: MaintenanceWindowRequest): Promise<MaintenanceWindow> {
	const res = await apiFetch(`${API_BASE_URL}/api/maintenance-windows`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedMaintenanceWindowRequest.serialize(toGeneratedRequest(request)))
	});
	if (!res.ok) {
		throw await failure(res, 'POST /api/maintenance-windows');
	}
	return decodeMaintenanceWindow(res);
}

export async function updateMaintenanceWindow(id: string, request: MaintenanceWindowRequest): Promise<MaintenanceWindow> {
	const res = await apiFetch(`${API_BASE_URL}/api/maintenance-windows/${id}`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedMaintenanceWindowRequest.serialize(toGeneratedRequest(request)))
	});
	if (!res.ok) {
		throw await failure(res, `PUT /api/maintenance-windows/${id}`);
	}
	return decodeMaintenanceWindow(res);
}

/** 204 No Content on success. */
export async function deleteMaintenanceWindow(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/maintenance-windows/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw await failure(res, `DELETE /api/maintenance-windows/${id}`);
	}
}
