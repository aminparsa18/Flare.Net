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

import { aggregateLogs, getLogAttributeValues, type AttributeBag, type AttributeFilter } from '$lib/api';
import { getSpanAttributeValues, type SpanAttributeBag, type SpanAttributeFilter } from '$lib/traces-api';
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

/** A resolved parent for a chained (`dependsOnVariableId`) variable - the parent's own
 *  definition plus its currently-selected values (never empty: an "All" parent selection is
 *  never passed in here; the caller resolves to `undefined` instead, same as having no
 *  parent). More than one value only for a `multi` parent. */
export interface VariableDependency {
	variable: DashboardVariable;
	values: string[];
}

/** A variable's session-start selection - `defaultValues` for a `multi` variable,
 *  `defaultValue` otherwise, `[]` ("All") when neither is set. */
export function defaultSelection(variable: DashboardVariable): string[] {
	if (variable.multi) return [...(variable.defaultValues ?? [])];
	return variable.defaultValue ? [variable.defaultValue] : [];
}

/** One attribute constraint matching any of `values` - a plain `Equals` for a single value
 *  (the exact shape every filter had before multi-value variables existed), `In` for more. */
function attributeMatch<B>(bag: B, key: string, values: string[]): { bag: B; key: string; value: string; operator?: 'In'; values?: string[] } {
	return values.length > 1 ? { bag, key, value: '', operator: 'In', values } : { bag, key, value: values[0] };
}

/**
 * Extra `services`/`attributes` narrowing a chained variable's own wide-window query should
 * carry from its resolved parent - `{}` (no narrowing, same as an unchained variable) when
 * `dependency` is absent *or* when the parent's target/bag isn't expressible against
 * `endpoint` (e.g. a `Span`-bag parent narrowing a Logs-endpoint child: Logs' `LogFilter` has
 * no `Span` bag to filter on). That silent fallback, not an error, mirrors this codebase's
 * existing "best-effort, missing input just means no narrowing" posture for variable
 * resolution in general.
 */
function dependencyNarrowing(
	dependency: VariableDependency | undefined,
	endpoint: 'Logs' | 'Traces'
): { services?: string[]; attributes?: ResolvedAttributeOverride[] } {
	if (!dependency) return {};
	const { variable, values } = dependency;
	if (variable.target === 'Service') return { services: values };
	const bag = variable.attributeBag;
	const key = variable.attributeKey?.trim();
	if (!bag || !key) return {};
	if (endpoint === 'Logs' && bag === 'Span') return {};
	if (endpoint === 'Traces' && bag === 'Log') return {};
	return { attributes: [{ bag, key, values }] };
}

/**
 * Resolves a `Query`-sourced variable's selectable values, narrowed by `dependency` (its
 * resolved parent - see `DashboardVariable.dependsOnVariableId`) when one is given. Never
 * called for a `Custom` variable - the caller should read `variable.customValues` directly
 * instead, so a custom variable's options never depend on network state at all.
 *
 * Best-effort, same posture as the old `loadKnownServices`: a failed lookup (or an
 * `Attribute` variable with no key typed yet) resolves to no options rather than
 * surfacing an error - the picker just shows only "All" until a retry.
 */
export async function resolveQueryVariableOptions(variable: DashboardVariable, dependency?: VariableDependency | null): Promise<string[]> {
	const dep = dependency ?? undefined;
	try {
		if (variable.target === 'Service') {
			// Service is always resolved against Logs' own aggregate regardless of which panel
			// types the resolved value ends up applied to (see the module header comment) - so a
			// dependency here is narrowed the same "Logs endpoint" way an Attribute/Log|Resource|
			// Scope variable's own query below is.
			const narrowing = dependencyNarrowing(dep, 'Logs');
			const filter = { ...wideRange(), services: narrowing.services, attributes: narrowing.attributes?.map((a) => attributeMatch(a.bag as AttributeBag, a.key, a.values)) };
			const res = await aggregateLogs({ filter, bucketWidthSeconds: WINDOW_MS / 1000, groupBy: 'Service' });
			return [...new Set(res.buckets.map((b) => b.groupKey).filter((k): k is string => !!k))].sort();
		}
		const key = variable.attributeKey?.trim();
		const bag = variable.attributeBag;
		if (!key || !bag) return [];
		if (bag === 'Span') {
			const narrowing = dependencyNarrowing(dep, 'Traces');
			const filter = {
				...wideRange(),
				services: narrowing.services,
				attributes: narrowing.attributes?.map((a) => attributeMatch(a.bag as 'Span' | 'Resource' | 'Scope', a.key, a.values))
			};
			const res = await getSpanAttributeValues({ filter, bag: 'Span', key, limit: 50 });
			return res.values.map((v) => v.value);
		}
		const narrowing = dependencyNarrowing(dep, 'Logs');
		const filter = {
			...wideRange(),
			services: narrowing.services,
			attributes: narrowing.attributes?.map((a) => attributeMatch(logsAttributeBag(a.bag), a.key, a.values))
		};
		const res = await getLogAttributeValues({ filter, bag: logsAttributeBag(bag), key, limit: 50 });
		return res.values.map((v) => v.value);
	} catch {
		return [];
	}
}

/** One resolved attribute constraint ready to append to a panel's own `attributeFilters` -
 *  matches any of `values` (never empty; more than one only for a `multi` variable). Turned
 *  into a concrete `Equals`/`In` filter by `attributesForLogsPanel`/`attributesForTracesPanel`
 *  below, each of which also drops bags its own panel type can't express. */
export interface ResolvedAttributeOverride {
	bag: DashboardAttributeBag;
	key: string;
	values: string[];
}

/** Every currently-active override a dashboard's variables produce for one panel, already
 *  resolved from `DashboardVariable[]` + the session's selection map (and that panel's own
 *  `excludedVariableIds`) - computed per panel inside `DashboardPanelCard.svelte` and handed
 *  straight down to that panel's own body, replacing Phase 4's single
 *  `serviceOverride: string | null` prop. */
export interface ResolvedVariableOverrides {
	/** Every selected value of every `Service`-target variable, in definition order - passed
	 *  to `explorer.setServices` as-is, so more than one selected service (from one `multi`
	 *  variable or from several variables) behaves as an OR-list rather than one silently
	 *  clobbering another. */
	services: string[];
	/** Every `Attribute`-target variable with a selection (not "All") - a panel body
	 *  further filters this by which bags its own panel type actually supports (see
	 *  `attributesForLogsPanel`/`attributesForTracesPanel` below) before applying it. */
	attributes: ResolvedAttributeOverride[];
}

/** Builds `ResolvedVariableOverrides` from a dashboard's variable definitions plus the
 *  viewer's session-only selection map (`DashboardViewerState.variableValues`) - a missing
 *  or empty selection means "All", same "off means don't touch this filter at all" rule the
 *  old `serviceOverride` used.
 *
 *  `excludedVariableIds` (a panel's own `DashboardPanel.excludedVariableIds`, or `[]` for
 *  the dashboard-wide "every applicable variable narrows this panel" default) drops the
 *  listed variables entirely, as if this one panel simply had no selection for them - the
 *  per-panel opt-out. Called once per panel (see `DashboardPanelCard.svelte`), each with
 *  that panel's own exclusions, rather than once dashboard-wide the way it was before that
 *  feature existed. */
export function resolveVariableOverrides(
	variables: DashboardVariable[],
	selections: Record<string, string[]>,
	excludedVariableIds: readonly string[] = []
): ResolvedVariableOverrides {
	const excluded = excludedVariableIds.length ? new Set(excludedVariableIds) : null;
	const services: string[] = [];
	const attributes: ResolvedAttributeOverride[] = [];
	for (const variable of variables) {
		if (excluded?.has(variable.id)) continue;
		const values = selections[variable.id];
		if (!values?.length) continue;
		if (variable.target === 'Service') {
			services.push(...values);
		} else if (variable.attributeBag && variable.attributeKey?.trim()) {
			attributes.push({ bag: variable.attributeBag, key: variable.attributeKey.trim(), values });
		}
	}
	return { services, attributes };
}

/** Narrows `overrides.attributes` to the ones a Logs panel's `AttributeFilter[]` can
 *  actually express - `Log`/`Resource`/`Scope` map straight onto Logs' own `AttributeBag`,
 *  `Span` is dropped (a Traces-only bag, see `DashboardAttributeBag`'s own remarks). */
export function attributesForLogsPanel(overrides: ResolvedVariableOverrides): AttributeFilter[] {
	return overrides.attributes.filter((a) => a.bag !== 'Span').map((a) => attributeMatch(a.bag as AttributeBag, a.key, a.values));
}

/** Narrows `overrides.attributes` to the ones a Traces panel's `SpanAttributeFilter[]` can
 *  actually express - `Span`/`Resource`/`Scope` map straight onto Traces' own
 *  `SpanAttributeBag`, `Log` is dropped (a Logs-only bag). */
export function attributesForTracesPanel(overrides: ResolvedVariableOverrides): SpanAttributeFilter[] {
	return overrides.attributes.filter((a) => a.bag !== 'Log').map((a) => attributeMatch(a.bag as SpanAttributeBag, a.key, a.values));
}
