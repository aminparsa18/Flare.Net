// Client for Flare.Api's saved-views API (named, reloadable, shareable per-page filter/
// selection state). Field names/casing/enum values are a hand-mirror of
// src/Flare.Api/Model/SavedViewModels.cs + Json/SavedViewsJsonContext.cs (camelCase
// properties, PascalCase string enums - same convention `alerts-api.ts` documents).
// Keep in sync with those files by hand.
//
// `state` is deliberately typed `unknown` on the wire, not one of the per-page filter
// interfaces - Flare.Api never interprets it (round-trips it as an opaque JsonElement,
// see SavedView's C# remarks), and which shape it actually is depends on `pageType`. Each
// explorer state class's own toSavedViewState()/applySavedViewState() narrows it.

import { API_BASE_URL } from './api';

// ---- Shared shapes (SavedViewModels.cs) ------------------------------------

export type PageType = 'Logs' | 'Traces' | 'Metrics';

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

// ---- CRUD --------------------------------------------------------------------

/** `pageType` scopes the list to one dashboard page's own view picker; omitted lists every view (the `/views` management page). */
export async function listSavedViews(pageType?: PageType, signal?: AbortSignal): Promise<SavedViewListResponse> {
	const url = pageType ? `${API_BASE_URL}/api/views?pageType=${pageType}` : `${API_BASE_URL}/api/views`;
	const res = await fetch(url, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/views failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

export async function getSavedView(id: string, signal?: AbortSignal): Promise<SavedView> {
	const res = await fetch(`${API_BASE_URL}/api/views/${id}`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/views/${id} failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

export async function createSavedView(request: SavedViewRequest): Promise<SavedView> {
	const res = await fetch(`${API_BASE_URL}/api/views`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`POST /api/views failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

export async function updateSavedView(id: string, request: SavedViewRequest): Promise<SavedView> {
	const res = await fetch(`${API_BASE_URL}/api/views/${id}`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request)
	});
	if (!res.ok) {
		throw new Error(`PUT /api/views/${id} failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

/** 204 No Content on success - unlike every other function here, there's no JSON body to return. */
export async function deleteSavedView(id: string): Promise<void> {
	const res = await fetch(`${API_BASE_URL}/api/views/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/views/${id} failed: ${res.status} ${res.statusText}`);
	}
}
