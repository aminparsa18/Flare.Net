// Resolves a dashboard variable's *selectable values* (see
// docs-internal/adr/0025-dashboard-variables.md) and applies its *currently-selected*
// value onto one panel's own explorer state. Split from DashboardViewerState (which just
// owns the definitions + the session-only selection map) so both halves - option
// resolution here, application in the panel bodies - have one place to live instead of
// being duplicated per panel type.
//
// No new API endpoint exists for any of this: `Service` reuses the same wide-window
// aggregate `DashboardViewerState`'s old `loadKnownServices` (and `LogsExplorerState`'s own
// copy) already ran; `Attribute` reuses the same `/api/logs/attribute-values` /
// `/api/spans/attribute-values` endpoints AttributeFiltersRow/SpanAttributeFiltersRow's own
// value autocomplete already calls.

import { aggregateLogs, getLogAttributeValues, type AttributeBag } from '$lib/api';
import { getSpanAttributeValues } from '$lib/traces-api';
import type { DashboardAttributeBag, DashboardVariable } from '$lib/dashboards-api';

/** Same wide window `loadKnownServices`/AlertRuleFormDialog's own copy use to enumerate
 *  values independent of whatever time range each panel happens to be showing. */
const WINDOW_MS = 7 * 24 * 60 * 60 * 1000;

function wideRange(): { from: string; to: string } {
	const to = new Date();
	const from = new Date(to.getTime() - WINDOW_MS);
	return { from: from.toISOString(), to: to.toISOString() };
}

/** `Resource`/`Scope` attributes are the same ingest-time-correlated bag on both a log
 *  record and the span it's correlated with (see `DashboardAttributeBag`'s own remarks) -
 *  queried against Logs here regardless of which panel types the resolved value ends up
 *  applied to. Only `Log` and `Span` are panel-type-specific record bags with their own
 *  endpoint. */
function logsAttributeBag(bag: DashboardAttributeBag): AttributeBag {
	return bag === 'Span' ? 'Log' : bag;
}

/**
 * Resolves a `Query`-sourced variable's selectable values. Never called for a `Custom`
 * variable - the caller should read `variable.customValues` directly instead, so a custom
 * variable's options never depend on network state at all.
 *
 * Best-effort, same posture as the old `loadKnownServices`: a failed lookup (or an
 * `Attribute` variable with no key typed yet) resolves to no options rather than
 * surfacing an error - the picker just shows only "All" until a retry.
 */
export async function resolveQueryVariableOptions(variable: DashboardVariable): Promise<string[]> {
	try {
		if (variable.target === 'Service') {
			const res = await aggregateLogs({ filter: wideRange(), bucketWidthSeconds: WINDOW_MS / 1000, groupBy: 'Service' });
			return [...new Set(res.buckets.map((b) => b.groupKey).filter((k): k is string => !!k))].sort();
		}
		const key = variable.attributeKey?.trim();
		const bag = variable.attributeBag;
		if (!key || !bag) return [];
		if (bag === 'Span') {
			const res = await getSpanAttributeValues({ filter: wideRange(), bag: 'Span', key, limit: 50 });
			return res.values.map((v) => v.value);
		}
		const res = await getLogAttributeValues({ filter: wideRange(), bag: logsAttributeBag(bag), key, limit: 50 });
		return res.values.map((v) => v.value);
	} catch {
		return [];
	}
}

/** One resolved attribute-equality constraint ready to append to a panel's own
 *  `attributeFilters` - `bag` already narrowed to that panel type's own bag enum by
 *  `resolvedAttributesFor` below, never `Log`/`Span` mismatched against the wrong panel
 *  type. */
export interface ResolvedAttributeOverride {
	bag: DashboardAttributeBag;
	key: string;
	value: string;
}

/** Every currently-active override a dashboard's variables produce, already resolved from
 *  `DashboardVariable[]` + the session's selection map - the one shape threaded down
 *  through DashboardGrid -> DashboardPanelCard -> each panel body, replacing Phase 4's
 *  single `serviceOverride: string | null` prop. */
export interface ResolvedVariableOverrides {
	/** Every `Service`-target variable with a selected (non-"All") value, in definition
	 *  order - passed to `explorer.setServices` as-is, so more than one such variable
	 *  behaves as an OR-list rather than one silently clobbering another. */
	services: string[];
	/** Every `Attribute`-target variable with a selected (non-"All") value - a panel body
	 *  further filters this by which bags its own panel type actually supports (see
	 *  `attributesForLogsPanel`/`attributesForTracesPanel` below) before applying it. */
	attributes: ResolvedAttributeOverride[];
}

/** Builds `ResolvedVariableOverrides` from a dashboard's variable definitions plus the
 *  viewer's session-only selection map (`DashboardViewerState.variableValues`) - `null`/
 *  missing/`'__all__'` all mean "not selected", same "off means don't touch this filter at
 *  all" rule the old `serviceOverride` used. */
export function resolveVariableOverrides(variables: DashboardVariable[], values: Record<string, string | null>): ResolvedVariableOverrides {
	const services: string[] = [];
	const attributes: ResolvedAttributeOverride[] = [];
	for (const variable of variables) {
		const value = values[variable.id];
		if (!value) continue;
		if (variable.target === 'Service') {
			services.push(value);
		} else if (variable.attributeBag && variable.attributeKey?.trim()) {
			attributes.push({ bag: variable.attributeBag, key: variable.attributeKey.trim(), value });
		}
	}
	return { services, attributes };
}

/** Narrows `overrides.attributes` to the ones a Logs panel's `AttributeFilter[]` can
 *  actually express - `Log`/`Resource`/`Scope` map straight onto Logs' own `AttributeBag`,
 *  `Span` is dropped (a Traces-only bag, see `DashboardAttributeBag`'s own remarks). */
export function attributesForLogsPanel(overrides: ResolvedVariableOverrides): { bag: AttributeBag; key: string; value: string }[] {
	return overrides.attributes.filter((a) => a.bag !== 'Span').map((a) => ({ bag: a.bag as AttributeBag, key: a.key, value: a.value }));
}

/** Narrows `overrides.attributes` to the ones a Traces panel's `SpanAttributeFilter[]` can
 *  actually express - `Span`/`Resource`/`Scope` map straight onto Traces' own
 *  `SpanAttributeBag`, `Log` is dropped (a Logs-only bag). */
export function attributesForTracesPanel(overrides: ResolvedVariableOverrides): { bag: 'Span' | 'Resource' | 'Scope'; key: string; value: string }[] {
	return overrides.attributes.filter((a) => a.bag !== 'Log').map((a) => ({ bag: a.bag as 'Span' | 'Resource' | 'Scope', key: a.key, value: a.value }));
}
