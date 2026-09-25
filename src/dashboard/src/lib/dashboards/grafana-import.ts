// Best-effort import of a Grafana dashboard export (roadmap's "Lower-priority nice-to-have:
// importing Grafana dashboard JSON, easing migration for anyone coming from Grafana" -
// see docs-internal/planning/roadmap.md and signoz#1700, the prior-art commit this follows).
//
// This is deliberately *structural* import only: layout (grid position) + title + a
// best-effort panel-type mapping. There is no query translation, and there never will be a
// complete one here - a Grafana panel's `targets[].expr` is PromQL/LogQL/whatever its own
// datasource speaks, with zero correspondence to Flare's own LogFilter/TracesFilter/
// MetricsFilter shapes over ClickHouse (ADR-0012's "one protocol in: OTLP only" cuts the
// other way too: Flare has no PromQL/LogQL engine to interpret a foreign query against).
// So every imported panel's `query` is reset to that panel type's own blank-slate default -
// exactly what AddPanelDialog gives a brand-new panel before anything is configured (Metrics'
// `selectedMetric: null` already renders MetricChart's own "pick a metric" empty state, so
// this is a real, supported state, not a hack) - and the caller surfaces a summary asking
// the user to open each panel and set what it shows. What *does* carry over structurally:
// panel titles, panel type (mapped to the nearest of Flare's three), and grid position/size
// (rescaled from Grafana's 24-column grid to GRID_COLUMNS below).
//
// Grafana's schema wraps a dashboard two ways depending on where the JSON came from: a
// straight "Export as JSON" from the UI is the dashboard object itself (`{title, panels,
// ...}`); the HTTP API (`GET /api/dashboards/uid/:uid`) wraps it as `{dashboard: {...},
// meta: {...}}`. Both are accepted here (see the `dashboard` unwrap below).
//
// Panels inside a *collapsed* row aren't top-level siblings - Grafana nests them under that
// row panel's own `panels` array instead, with their `gridPos` already in the same absolute
// coordinate space as everything else (so they still tile correctly once uncollapsed). An
// *expanded* row's children are already flat siblings. flattenPanels() below handles both by
// recursing into any `type: "row"` panel's nested `panels` and dropping the row panel itself
// (it's a grouping header, not a real widget - never counted as skipped/unsupported).

import { GRID_COLUMNS, nextPanelPosition } from './layout';
import type { DashboardLayout, DashboardPanel, PanelType } from '$lib/dashboards-api';
import type { LogsSavedViewState } from '$lib/logs/state.svelte';
import type { TracesSavedViewState } from '$lib/traces/state.svelte';
import type { MetricsSavedViewState } from '$lib/metrics/state.svelte';

/** Grafana's dashboard grid is always 24 columns wide, regardless of screen size - fixed in its own schema, unlike GRID_COLUMNS which is this app's own gridstack config. */
const GRAFANA_GRID_COLUMNS = 24;

/** Grafana panel `type` values that are fundamentally a metric/number/time-series chart - the only shape Flare's own "Metrics" panel (a ClickHouse metric query + chart) can stand in for. */
const METRICS_TYPES = new Set(['timeseries', 'graph', 'stat', 'gauge', 'bargauge', 'barchart', 'piechart']);
/** `table` is a guess, not a sure thing - Grafana table panels can be backed by any datasource, but in an observability dashboard they're most often a raw list of log rows, which is what Flare's own "Logs" panel shows. */
const LOGS_TYPES = new Set(['logs', 'table']);
/** Only newer Grafana (Tempo's trace panel) has a dedicated type for this - most Grafana dashboards predate it and have no trace panel at all. */
const TRACES_TYPES = new Set(['traces']);

function mapPanelType(type: string | undefined): PanelType | null {
	if (!type) return null;
	if (METRICS_TYPES.has(type)) return 'Metrics';
	if (LOGS_TYPES.has(type)) return 'Logs';
	if (TRACES_TYPES.has(type)) return 'Traces';
	return null;
}

/** Blank-slate query for a freshly-imported panel of `panelType` - identical to what a brand-new panel of that type gets from AddPanelDialog before the user configures anything (see this module's header comment for why nothing more specific can be filled in here). */
function defaultQueryFor(panelType: PanelType): unknown {
	switch (panelType) {
		case 'Logs':
			return {
				timeRangePreset: '1h',
				customRange: null,
				services: [],
				severityNumbers: [],
				search: '',
				attributeFilters: [],
				bodyJsonFilters: [],
				scopeNames: [],
				postProcessFunctions: [],
				timeShiftSeconds: null,
				maxLinesPerRow: 1
			} satisfies LogsSavedViewState;
		case 'Traces':
			return { timeRangePreset: '1h', services: [], attributeFilters: [] } satisfies TracesSavedViewState;
		case 'Metrics':
			// topN: 20 mirrors metrics/state.svelte.ts's own DEFAULT_TOP_N - not imported from
			// there for the same "no shared config between Flare.Api and the dashboard, and
			// this module shouldn't pull in the whole Metrics explorer state class just for one
			// constant" reasoning that file's own comment gives for not importing its C# twin.
			return {
				timeRangePreset: '1h',
				customRange: null,
				services: [],
				compareEnabled: false,
				groupByAttributeKey: null,
				topN: 20,
				havingOperator: null,
				havingValue: null,
				postProcessFunctions: [],
				timeShiftSeconds: null,
				selectedMetric: null
			} satisfies MetricsSavedViewState;
	}
}

interface GrafanaPanel {
	type?: unknown;
	title?: unknown;
	gridPos?: unknown;
	panels?: unknown;
}

function isGrafanaPanel(value: unknown): value is GrafanaPanel {
	return value != null && typeof value === 'object';
}

/** Recursively drops `row` panels, keeping their nested children (if any) in their place - see this module's header comment for why. */
function flattenPanels(panels: unknown[]): GrafanaPanel[] {
	const out: GrafanaPanel[] = [];
	for (const raw of panels) {
		if (!isGrafanaPanel(raw)) continue;
		if (raw.type === 'row') {
			if (Array.isArray(raw.panels)) out.push(...flattenPanels(raw.panels));
			continue;
		}
		out.push(raw);
	}
	return out;
}

/** Rescales a Grafana `gridPos` (24-column grid) to this app's GRID_COLUMNS grid, clamping so a panel can never hang off the right edge or collapse to nothing. Approximate, not pixel-exact - Grafana's and gridstack's row-height units don't match 1:1 either, and there's no way to make an import of someone else's dashboard look identical anyway. Falls back to `nextPanelPosition` (same non-overlapping stacking a brand-new panel gets) when `gridPos` is missing or malformed. */
function convertGridPos(gridPos: unknown, existing: readonly DashboardPanel[]): DashboardPanel['layout'] {
	if (
		gridPos == null ||
		typeof gridPos !== 'object' ||
		!(['x', 'y', 'w', 'h'] as const).every((key) => typeof (gridPos as Record<string, unknown>)[key] === 'number')
	) {
		return nextPanelPosition(existing);
	}
	const g = gridPos as { x: number; y: number; w: number; h: number };
	const scale = GRID_COLUMNS / GRAFANA_GRID_COLUMNS;
	const w = Math.min(GRID_COLUMNS, Math.max(1, Math.round(g.w * scale)));
	const x = Math.min(GRID_COLUMNS - w, Math.max(0, Math.round(g.x * scale)));
	const h = Math.max(2, Math.round(g.h * scale));
	const y = Math.max(0, Math.round(g.y * scale));
	return { x, y, w, h };
}

export interface GrafanaImportResult {
	name: string;
	layout: DashboardLayout;
	/** How many panels were mapped to a Flare panel type and included. */
	importedCount: number;
	/** How many panels were dropped because their Grafana `type` has no Flare equivalent. */
	skippedCount: number;
	/** Distinct skipped `type` values, for a "unsupported: heatmap, text" summary - deduplicated and sorted so the message is stable across imports of the same file. */
	skippedTypes: string[];
}

/**
 * Converts a Grafana dashboard export into a Flare `DashboardLayout` - or returns `null` when
 * `parsed` doesn't even look like one (no `panels` array anywhere), so the caller can fall
 * back to its own "unrecognized file" error instead of reporting a confusing zero-panel
 * "import" of something that was never a dashboard at all.
 */
export function parseGrafanaDashboard(parsed: unknown): GrafanaImportResult | null {
	if (parsed == null || typeof parsed !== 'object') return null;
	const wrapper = parsed as { dashboard?: unknown };
	const root = (wrapper.dashboard != null && typeof wrapper.dashboard === 'object' ? wrapper.dashboard : parsed) as {
		title?: unknown;
		panels?: unknown;
	};
	if (!Array.isArray(root.panels)) return null;

	const panels: DashboardPanel[] = [];
	const skippedTypes = new Set<string>();
	let skippedCount = 0;

	for (const raw of flattenPanels(root.panels)) {
		const rawType = typeof raw.type === 'string' ? raw.type : undefined;
		const panelType = mapPanelType(rawType);
		if (!panelType) {
			skippedCount++;
			if (rawType) skippedTypes.add(rawType);
			continue;
		}
		panels.push({
			id: crypto.randomUUID(),
			panelType,
			title: typeof raw.title === 'string' && raw.title.trim() ? raw.title : panelType,
			layout: convertGridPos(raw.gridPos, panels),
			query: defaultQueryFor(panelType)
		});
	}

	return {
		name: typeof root.title === 'string' && root.title.trim() ? root.title : 'Imported dashboard',
		layout: { panels, variables: [] },
		importedCount: panels.length,
		skippedCount,
		skippedTypes: [...skippedTypes].sort()
	};
}
