// Client for Flare.Api's saved-views API (named, reloadable, shareable per-page filter/
// selection state).
//
// Migrated (Phase 2 of docs-internal/investigations/memorypack-serialization-migration-scope.md)
// to MemoryPack - see `auth-api.ts`'s header comment for the general shape. Every type here
// is hand-written (`$lib/memorypack/`): `SavedView`/`SavedViewRequest` carry the opaque
// `JsonElement State` blob MemoryPack has no native mapping for at all (see
// `$lib/memorypack/SavedView.ts`'s header comment) on top of `DateTimeOffset`
// `createdAt`/`updatedAt`. `pageType` converts through `$lib/memorypack/enums.ts`'s
// `savedViewPageTypeToString`/`FromString`.
//
// `state` stays deliberately typed `unknown` on the wire, not one of the per-page filter
// interfaces - Flare.Api never interprets it (round-trips it as an opaque JsonElement, see
// SavedView's C# remarks), and which shape it actually is depends on `pageType`. Each
// explorer state class's own toSavedViewState()/applySavedViewState() narrows it.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import { savedViewPageTypeFromString, savedViewPageTypeToString, type SavedViewPageTypeName } from '$lib/memorypack/enums';
import { SavedView as GeneratedSavedView } from '$lib/memorypack/SavedView';
import { SavedViewRequest as GeneratedSavedViewRequest } from '$lib/memorypack/SavedViewRequest';
import { SavedViewListResponse as GeneratedSavedViewListResponse } from '$lib/memorypack/SavedViewListResponse';

// ---- Shared shapes (SavedViewModels.cs) ------------------------------------

export type PageType = SavedViewPageTypeName;

/** A named, reloadable snapshot of one dashboard page's filter/selection state. */
export interface SavedView {
	id: string;
	name: string;
	description: string;
	pageType: PageType;
	state: unknown;
	createdAt: string;
	updatedAt: string;
}

/** Create/update request body - same shape as `SavedView` minus the server-assigned fields. */
export interface SavedViewRequest {
	name: string;
	description?: string;
	pageType: PageType;
	state: unknown;
}

export interface SavedViewListResponse {
	views: SavedView[];
}

function toSavedView(dto: GeneratedSavedView): SavedView {
	return {
		id: dto.id,
		name: dto.name ?? '',
		description: dto.description ?? '',
		pageType: savedViewPageTypeToString(dto.pageType),
		state: dto.state,
		createdAt: dto.createdAt.toISOString(),
		updatedAt: dto.updatedAt.toISOString()
	};
}

async function decodeSavedView(res: Response): Promise<SavedView> {
	const dto = GeneratedSavedView.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding SavedView.');
	}
	return toSavedView(dto);
}

function toGeneratedSavedViewRequest(request: SavedViewRequest): GeneratedSavedViewRequest {
	const dto = new GeneratedSavedViewRequest();
	dto.name = request.name;
	dto.description = request.description ?? null;
	dto.pageType = savedViewPageTypeFromString(request.pageType);
	dto.state = request.state;
	return dto;
}

// ---- CRUD --------------------------------------------------------------------

/** `pageType` scopes the list to one dashboard page's own view picker; omitted lists every view (the `/views` management page). */
export async function listSavedViews(pageType?: PageType, signal?: AbortSignal): Promise<SavedViewListResponse> {
	const url = pageType ? `${API_BASE_URL}/api/views?pageType=${pageType}` : `${API_BASE_URL}/api/views`;
	const res = await apiFetch(url, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/views failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedSavedViewListResponse.deserialize(await res.arrayBuffer());
	return { views: (dto?.views ?? []).map((v) => toSavedView(v!)) };
}

export async function getSavedView(id: string, signal?: AbortSignal): Promise<SavedView> {
	const res = await apiFetch(`${API_BASE_URL}/api/views/${id}`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/views/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodeSavedView(res);
}

export async function createSavedView(request: SavedViewRequest): Promise<SavedView> {
	const dto = toGeneratedSavedViewRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/views`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSavedViewRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/views failed: ${res.status} ${res.statusText}`);
	}
	return decodeSavedView(res);
}

export async function updateSavedView(id: string, request: SavedViewRequest): Promise<SavedView> {
	const dto = toGeneratedSavedViewRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/views/${id}`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedSavedViewRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/views/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodeSavedView(res);
}

/** 204 No Content on success - unlike every other function here, there's no body to decode. */
export async function deleteSavedView(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/views/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/views/${id} failed: ${res.status} ${res.statusText}`);
	}
}
