// Client for Flare.Api's dashboards API (named, multi-panel dashboards composed from
// arbitrary log/trace/metric queries - see docs-internal/adr/0023-custom-dashboards.md).
//
// Same MemoryPack shape as `saved-views-api.ts` (see that file's header comment for the
// general convention): `Dashboard`/`DashboardRequest` carry the opaque `JsonElement
// LayoutJson` blob MemoryPack has no native mapping for, on top of `DateTimeOffset`
// `createdAt`/`updatedAt` - both hand-written companions (`$lib/memorypack/Dashboard.ts`).
//
// `layoutJson` stays deliberately typed as `DashboardLayout` (this module's own shape, not
// generated) on the wire - Flare.Api never interprets it (round-trips it as an opaque
// JsonElement, see Dashboard's C# remarks). It's the one place this client *does* type the
// JSON payload, since - unlike a SavedView's per-page state, which varies by pageType and
// is owned by each Explorer page's own state module - a dashboard's layout has one fixed
// shape everywhere: an array of panels, each embedding one Explorer page's saved-search
// state verbatim.

import { API_BASE_URL, apiFetch, memoryPackAcceptHeaders, memoryPackBody, memoryPackRequestHeaders } from './api';
import { Dashboard as GeneratedDashboard } from '$lib/memorypack/Dashboard';
import { DashboardRequest as GeneratedDashboardRequest } from '$lib/memorypack/DashboardRequest';
import { DashboardListResponse as GeneratedDashboardListResponse } from '$lib/memorypack/DashboardListResponse';

// ---- Shared shapes (DashboardModels.cs) ------------------------------------

/** Which Explorer page a panel's embedded `query` came from - same three values as `SavedViewPageType`. */
export type PanelType = 'Logs' | 'Traces' | 'Metrics';

/** One widget on a dashboard. `query` is that panel type's own `*FilterState` shape (see `$lib/logs/state.svelte.ts` etc.) - opaque here, exactly as a `SavedView.state` is opaque to this client too. */
export interface DashboardPanel {
	id: string;
	panelType: PanelType;
	title: string;
	layout: { x: number; y: number; w: number; h: number };
	query: unknown;
}

/** The parsed shape of a `Dashboard`'s opaque `layoutJson` blob. */
export interface DashboardLayout {
	panels: DashboardPanel[];
}

/** A named, multi-panel dashboard. */
export interface DashboardSummary {
	id: string;
	name: string;
	description: string;
	layout: DashboardLayout;
	createdAt: string;
	updatedAt: string;
}

/** Create/update request body - same shape as `DashboardSummary` minus the server-assigned fields. */
export interface DashboardRequest {
	name: string;
	description?: string;
	layout: DashboardLayout;
}

export interface DashboardListResponse {
	dashboards: DashboardSummary[];
}

const EMPTY_LAYOUT: DashboardLayout = { panels: [] };

function parseLayout(raw: unknown): DashboardLayout {
	if (raw != null && typeof raw === 'object' && Array.isArray((raw as DashboardLayout).panels)) {
		return raw as DashboardLayout;
	}
	return EMPTY_LAYOUT;
}

function toDashboardSummary(dto: GeneratedDashboard): DashboardSummary {
	return {
		id: dto.id,
		name: dto.name ?? '',
		description: dto.description ?? '',
		layout: parseLayout(dto.layoutJson),
		createdAt: dto.createdAt.toISOString(),
		updatedAt: dto.updatedAt.toISOString()
	};
}

async function decodeDashboard(res: Response): Promise<DashboardSummary> {
	const dto = GeneratedDashboard.deserialize(await res.arrayBuffer());
	if (dto == null) {
		throw new Error('Empty response body decoding Dashboard.');
	}
	return toDashboardSummary(dto);
}

function toGeneratedDashboardRequest(request: DashboardRequest): GeneratedDashboardRequest {
	const dto = new GeneratedDashboardRequest();
	dto.name = request.name;
	dto.description = request.description ?? null;
	dto.layoutJson = request.layout;
	return dto;
}

// ---- CRUD --------------------------------------------------------------------

export async function listDashboards(signal?: AbortSignal): Promise<DashboardListResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/dashboards`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/dashboards failed: ${res.status} ${res.statusText}`);
	}
	const dto = GeneratedDashboardListResponse.deserialize(await res.arrayBuffer());
	return { dashboards: (dto?.dashboards ?? []).map((d) => toDashboardSummary(d!)) };
}

export async function getDashboard(id: string, signal?: AbortSignal): Promise<DashboardSummary> {
	const res = await apiFetch(`${API_BASE_URL}/api/dashboards/${id}`, { headers: memoryPackAcceptHeaders(), signal });
	if (!res.ok) {
		throw new Error(`GET /api/dashboards/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodeDashboard(res);
}

export async function createDashboard(request: DashboardRequest): Promise<DashboardSummary> {
	const dto = toGeneratedDashboardRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/dashboards`, {
		method: 'POST',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedDashboardRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`POST /api/dashboards failed: ${res.status} ${res.statusText}`);
	}
	return decodeDashboard(res);
}

export async function updateDashboard(id: string, request: DashboardRequest): Promise<DashboardSummary> {
	const dto = toGeneratedDashboardRequest(request);
	const res = await apiFetch(`${API_BASE_URL}/api/dashboards/${id}`, {
		method: 'PUT',
		headers: memoryPackRequestHeaders(),
		body: memoryPackBody(GeneratedDashboardRequest.serialize(dto))
	});
	if (!res.ok) {
		throw new Error(`PUT /api/dashboards/${id} failed: ${res.status} ${res.statusText}`);
	}
	return decodeDashboard(res);
}

/** 204 No Content on success - unlike every other function here, there's no body to decode. */
export async function deleteDashboard(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/dashboards/${id}`, { method: 'DELETE' });
	if (!res.ok) {
		throw new Error(`DELETE /api/dashboards/${id} failed: ${res.status} ${res.statusText}`);
	}
}
