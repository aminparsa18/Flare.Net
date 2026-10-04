// Mirrors a dashboard viewer's session-only state (selected variable values and the
// dashboard-wide time-range override) into the page URL, so a shared `/dashboards/<id>` link
// opens with the sender's selections instead of the recipient's defaults. Pure - no
// `$app/*` - so the encoding stays testable; DashboardViewerState.load() hydrates from it and
// routes/dashboards/[id]/+page.svelte writes it back with replaceState.
//
// Shape: `?range=1h&var-<variableId>=a&var-<variableId>=b`. Values are repeated params, not
// comma-joined, because a value (e.g. an attribute value) may itself contain a comma. An
// explicit "All" selection is `var-<id>=` (empty) so it can be told apart from "param absent,
// keep the variable's own default".
import type { DashboardVariable } from '$lib/dashboards-api';
import { TIME_RANGE_PRESETS, type TimeRangePreset } from '$lib/logs/time-range';
import { defaultSelection } from './variables';

const VAR_PREFIX = 'var-';
const RANGE_PARAM = 'range';
const RANGE_OFF = 'off';

/** Fixed-duration presets only - the override can't express an absolute custom range. */
const OVERRIDE_PRESETS = new Set<string>(TIME_RANGE_PRESETS.filter((p) => p.value !== 'custom').map((p) => p.value));

export interface DashboardUrlState {
	range: TimeRangePreset | null;
	/** `?range=off`: an explicit "each panel's own range", overriding a saved default. */
	rangeOff: boolean;
	/** Only variables the URL actually mentions; absent ids keep their default. */
	variableValues: Record<string, string[]>;
}

export function parseDashboardUrlState(search: URLSearchParams, variables: DashboardVariable[]): DashboardUrlState {
	const rawRange = search.get(RANGE_PARAM);
	const range = rawRange && OVERRIDE_PRESETS.has(rawRange) ? (rawRange as TimeRangePreset) : null;

	const rangeOff = rawRange === RANGE_OFF;

	const variableValues: Record<string, string[]> = {};
	for (const variable of variables) {
		const key = VAR_PREFIX + variable.id;
		if (!search.has(key)) continue;
		const values = search.getAll(key).filter((v) => v !== '');
		variableValues[variable.id] = variable.multi ? values : values.slice(0, 1);
	}
	return { range, rangeOff, variableValues };
}

/** Only state that differs from the dashboard's own defaults is written, keeping links short. */
export function buildDashboardUrlSearch(
	current: URLSearchParams,
	range: TimeRangePreset | null,
	variables: DashboardVariable[],
	variableValues: Record<string, string[]>,
	defaultRange: TimeRangePreset | null = null
): string {
	const next = new URLSearchParams(current);
	for (const key of [...next.keys()]) {
		if (key === RANGE_PARAM || key.startsWith(VAR_PREFIX)) next.delete(key);
	}
	if (range && range !== defaultRange) next.set(RANGE_PARAM, range);
	// No override while a default is saved must be spelled out, or the link would reopen at the default.
	else if (!range && defaultRange) next.set(RANGE_PARAM, RANGE_OFF);
	for (const variable of variables) {
		const values = variableValues[variable.id];
		if (!values) continue;
		const defaults = defaultSelection(variable);
		if (values.length === defaults.length && values.every((v, i) => v === defaults[i])) continue;
		if (values.length === 0) next.append(VAR_PREFIX + variable.id, '');
		else for (const v of values) next.append(VAR_PREFIX + variable.id, v);
	}
	const qs = next.toString();
	return qs ? `?${qs}` : '';
}
