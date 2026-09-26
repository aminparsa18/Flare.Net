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
import type { PanelThreshold } from '$lib/dashboards/thresholds';
import type { PanelReducer, PanelVisualization } from '$lib/dashboards/visualization';

// ---- Shared shapes (DashboardModels.cs) ------------------------------------

/** Which Explorer page a panel's embedded `query` came from - same three values as `SavedViewPageType`. */
export type PanelType = 'Logs' | 'Traces' | 'Metrics';

/** One widget on a dashboard. `query` is that panel type's own `*FilterState` shape (see `$lib/logs/state.svelte.ts` etc.) - opaque here, exactly as a `SavedView.state` is opaque to this client too. */
export interface DashboardPanel {
	id: string;
	panelType: PanelType;
	title: string;
	/**
	 * Optional free-text explanation of what this panel shows (roadmap's "Per-panel
	 * descriptions on dashboards" item) - rendered as an info icon next to the title, with
	 * the text in its tooltip. `undefined`/empty means none, same as before this field
	 * existed. Plain text, never rendered as HTML/Markdown.
	 */
	description?: string;
	layout: { x: number; y: number; w: number; h: number };
	query: unknown;
	/**
	 * `id`s of this dashboard's `DashboardVariable`s this panel opts out of - an excluded
	 * variable's currently-selected value never narrows this panel's own query, even though
	 * it still narrows every other panel it's applicable to (roadmap's "Per-panel opt-out
	 * from a dashboard variable" item). `undefined`/`[]` means "narrowed by every applicable
	 * variable, same as before this field existed" - `resolveVariableOverrides` in
	 * `$lib/dashboards/variables.ts` is where the exclusion is actually applied. An id left
	 * over from a since-removed variable is harmless - filtering by id just never matches
	 * anything, same "stale reference, silently inert" posture `DashboardVariable.
	 * dependsOnVariableId` already has.
	 */
	excludedVariableIds?: string[];
	/**
	 * Soft Y-axis floor/ceiling for a `Metrics` panel's chart (roadmap's "Soft Y-axis
	 * min/max on metric charts" item) - `undefined`/`null` on either means "auto" (the
	 * chart's own default, floored/ceilinged to the data itself, same as before this field
	 * existed). *Soft*: the axis still expands past a configured bound if the data actually
	 * exceeds it - it narrows the default view, it never clips a real point off the chart.
	 * Meaningless for `Logs`/`Traces` panels (VolumeChart has no configurable axis), same
	 * "field exists on the shared shape, only one panel type ever sets it" posture
	 * `excludedVariableIds` predates this with for variable-less dashboards. See
	 * MetricChart.svelte's `domainMin`/`domainMax` for how this is applied.
	 */
	yAxisMin?: number | null;
	yAxisMax?: number | null;
	/**
	 * Ordered visual threshold rules for a `Metrics` panel's chart (roadmap's "Per-panel
	 * visual thresholds / conditional formatting" item) - purely styling (a colored line +
	 * shaded region per rule, and the first matching rule's color on a hovered value),
	 * never a notification. `undefined`/`[]` means none, same as before this field existed.
	 * Meaningless for `Logs`/`Traces` panels, same posture as `yAxisMin`/`yAxisMax`. See
	 * `$lib/dashboards/thresholds.ts` for the shape and the first-match-wins precedence rule.
	 */
	thresholds?: PanelThreshold[];
	/**
	 * How a `Metrics` panel's result is drawn (roadmap's "Dashboard panel visualization
	 * types" item) - `panelType` stays the data source, this is switchable in place without
	 * touching `query`. `undefined` means `'timeSeries'` (the line chart every panel had before
	 * this existed); read it through `parseVisualization`, which also maps an unknown value to
	 * that default. Meaningless for `Logs`/`Traces` panels. See
	 * `$lib/dashboards/visualization.ts` and docs-internal/adr/0059-dashboard-panel-visualizations.md.
	 */
	visualization?: PanelVisualization;
	/**
	 * How each series collapses to one number for the `value`/`pie`/`table` visualizations.
	 * `undefined` means the result type's default (`sum` for a Sum metric, `avg` otherwise -
	 * see `defaultReducer`). Kept when switching to a visualization that doesn't use it, so
	 * switching back restores it.
	 */
	reducer?: PanelReducer;
	/**
	 * Per-column unit overrides for the `table` visualization, keyed by the column's reducer
	 * (`{ sum: 'By', avg: 'ms' }`). A missing key uses the metric's own unit. Read through
	 * `parseColumnUnits`. Kept when switching visualization, like `reducer`.
	 */
	columnUnits?: Partial<Record<PanelReducer, string>>;
	/**
	 * `id` of the `DashboardRow` this panel sits under, or `undefined`/`null` for the
	 * ungrouped area above every row (where every panel lived before rows existed).
	 * `layout.y` is relative to that row's own grid, not the whole dashboard - each row is
	 * its own gridstack instance (see docs-internal/adr/0054-dashboard-collapsible-rows.md).
	 * An id naming a since-removed row is treated as ungrouped, same "stale reference,
	 * silently inert" posture `excludedVariableIds` has.
	 */
	rowId?: string | null;
}

/**
 * A named, collapsible section of a dashboard owning every panel whose `rowId` names it
 * (roadmap's "Collapsible rows / panel groups on dashboards" item). Rows render in array
 * order, below the ungrouped panels. A collapsed row doesn't mount its panels at all, so
 * none of them run a query until it's expanded.
 */
export interface DashboardRow {
	id: string;
	title: string;
	/** Whether the row starts collapsed when the dashboard is opened. Toggling a row outside
	 *  edit mode is session-only and never changes this - see `DashboardViewerState.toggleRowCollapsed`. */
	collapsed?: boolean;
}

/**
 * Which bag a dashboard variable's attribute lives in - the same four bags Logs'
 * `AttributeBag` and Traces' `SpanAttributeBag` split between them (there's no single bag
 * enum that covers both: `Log`/`Span` are panel-type-specific record bags, `Resource`/
 * `Scope` are the ingest-time-correlated bags shared by both - see
 * docs-internal/adr/0025-dashboard-variables.md's "one bag enum, panel-type-scoped
 * applicability" decision). Determines both which panel types a resolved value gets
 * applied to (`Log` -> Logs panels only, `Span` -> Traces panels only, `Resource`/`Scope`
 * -> both) and, for a `Query`-sourced variable, which endpoint resolves its option list
 * (see `$lib/dashboards/variables.ts`'s `resolveQueryVariableOptions`).
 */
export type DashboardAttributeBag = 'Log' | 'Span' | 'Resource' | 'Scope';

/** What a dashboard variable's selected value narrows - `Service` mirrors Phase 4's fixed
 *  MVP override (applies to every panel type's own `services` filter), `Attribute` is the
 *  fuller "back something other than Service" case the roadmap left open, narrowed to one
 *  bag+key equality match (see `DashboardAttributeBag`'s own remarks on applicability). */
export type DashboardVariableTarget = 'Service' | 'Attribute';

/** Where a variable's selectable values come from - `Query` resolves them live (see
 *  `resolveQueryVariableOptions`), `Custom` is a fixed, hand-typed list. Session-only
 *  *selection* (which value is currently picked) still lives in `DashboardViewerState`,
 *  same as Phase 2's time-range override - only the variable's *definition* (this shape)
 *  is part of the saved dashboard. */
export type DashboardVariableSourceKind = 'Query' | 'Custom';

/**
 * One dashboard-wide, user-defined variable (see
 * docs-internal/adr/0025-dashboard-variables.md) - the fuller replacement for Phase 4's
 * single fixed built-in "Service" override. A dashboard can define any number of these;
 * each renders its own dropdown in the viewer's header, and its currently-selected value
 * (session-only, never part of this shape) narrows every applicable panel's query the same
 * "mutate the panel's own explorer state, let it re-run" way the old override did.
 */
export interface DashboardVariable {
	id: string;
	/** Display label shown in both the viewer's dropdown and the manage-variables list - freeform, not a token (no panel query is ever textually templated, so there's nothing for a `$name`-style identifier to be substituted into). */
	name: string;
	target: DashboardVariableTarget;
	/** Required when `target === 'Attribute'`; meaningless otherwise. */
	attributeBag?: DashboardAttributeBag;
	/** Required when `target === 'Attribute'`; meaningless otherwise. */
	attributeKey?: string;
	sourceKind: DashboardVariableSourceKind;
	/** Required when `sourceKind === 'Custom'`; ignored (a query resolves the list instead) when `sourceKind === 'Query'`. */
	customValues?: string[];
	/** Preselected value when the dashboard is first opened in a session, or `null`/omitted for "All" (no filter from this variable) by default. Ignored when `multi` is set - see `defaultValues`. */
	defaultValue?: string | null;
	/**
	 * Lets the viewer pick more than one value at once (a checkbox picker instead of a
	 * single-value dropdown) - e.g. scoping a dashboard to two services. Several selected
	 * values OR together: a `Service` variable passes all of them as the panel's `services`
	 * list, an `Attribute` variable becomes one `In` filter. Omitted/`false` for every
	 * variable saved before this existed. See docs-internal/adr/0058-multi-value-dashboard-variables.md.
	 */
	multi?: boolean;
	/** `multi` counterpart to `defaultValue` - preselected values, or omitted/`[]` for "All". */
	defaultValues?: string[];
	/**
	 * `id` of another variable in the same `DashboardLayout.variables` whose *currently
	 * selected* value(s) this variable's own `Query`-sourced options are resolved narrowed by
	 * (a `multi` parent with several values selected narrows by *any* of them)
	 * (chaining - see docs-internal/adr/0025-dashboard-variables.md's "not built" note and
	 * the roadmap item this closes). Ignored when `sourceKind === 'Custom'` (a fixed list has
	 * nothing to narrow) or when the referenced variable is currently unselected ("All") -
	 * in either case this variable's options fall back to the same unscoped wide window used
	 * before chaining existed. `$lib/dashboards/variables.ts#resolveQueryVariableOptions`
	 * does the actual narrowing; `DashboardViewerState` resolves parents before their
	 * dependents and re-resolves + revalidates every (transitive) dependent whenever the
	 * parent's own selected value changes.
	 */
	dependsOnVariableId?: string | null;
}

/** The parsed shape of a `Dashboard`'s opaque `layoutJson` blob. */
export interface DashboardLayout {
	panels: DashboardPanel[];
	/** Defaults to `[]` for any dashboard saved before this field existed - see `parseLayout`. */
	variables: DashboardVariable[];
	/** Absent (or `[]`) for a dashboard with no rows, including every one saved before rows existed. */
	rows?: DashboardRow[];
}

/** A named, multi-panel dashboard. */
export interface DashboardSummary {
	id: string;
	name: string;
	description: string;
	layout: DashboardLayout;
	createdAt: string;
	updatedAt: string;
	/** The creating user's id, or `null` for a dashboard created while auth was disabled, or
	 *  one that predates this field - see ADR-0027. A null owner means anyone Member-and-up
	 *  may still mutate it - `AuthState.canMutateDashboard` is the UI's own mirror of that
	 *  rule, `DashboardEndpoints.CanMutate` the server-enforced one. */
	ownerUserId: string | null;
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

const EMPTY_LAYOUT: DashboardLayout = { panels: [], variables: [] };

/** Exported so `DashboardsState.importDashboard()` can apply the same "malformed layout ->
 *  empty panels, don't throw" leniency to a hand-edited/corrupted import file that this
 *  module already applies to a server response's `layoutJson`. A dashboard saved before
 *  `variables` existed (or an imported Flare-export file predating it) has no such field at
 *  all - defaulted to `[]` here rather than rejected, same "additive, tolerant of older
 *  shapes" rule the ClickHouse migrations doc applies to storage. */
export function parseLayout(raw: unknown): DashboardLayout {
	if (raw != null && typeof raw === 'object' && Array.isArray((raw as DashboardLayout).panels)) {
		const layout = raw as Partial<DashboardLayout>;
		return {
			panels: layout.panels!,
			variables: Array.isArray(layout.variables) ? layout.variables : [],
			rows: Array.isArray(layout.rows) ? layout.rows : []
		};
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
		updatedAt: dto.updatedAt.toISOString(),
		ownerUserId: dto.ownerUserId
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
