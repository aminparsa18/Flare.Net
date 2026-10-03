// Search / filter / sort for the alert rules list, plus its URL encoding. Pure (no `$app/*`)
// so the logic stays testable; AlertRuleTable.svelte hydrates from the URL and writes back
// with replaceState.
//
// Shape: `?q=<name>&label=<key=value,...>&state=firing|ok|disabled|muted&kind=<AlertConditionKind>&sort=name|state|lastFired&dir=desc`.
// Absent params are the defaults (no filter, name ascending).
import type { AlertConditionKind, AlertRule, AlertRuleStatus } from '$lib/alerts-api';
import { labelsMatch, parseLabels } from '$lib/alerts/labels';

export type RuleStateFilter = 'all' | 'firing' | 'ok' | 'disabled' | 'muted';
export type RuleSortKey = 'name' | 'state' | 'lastFired';
export type RuleSortDirection = 'asc' | 'desc';

export interface AlertListView {
	query: string;
	/** `key=value, ...` - a rule must carry every pair (ADR-0084). '' = no label filter. */
	label: string;
	state: RuleStateFilter;
	kind: AlertConditionKind | 'all';
	sort: RuleSortKey;
	direction: RuleSortDirection;
}

export const DEFAULT_ALERT_LIST_VIEW: AlertListView = { query: '', label: '', state: 'all', kind: 'all', sort: 'name', direction: 'asc' };

export const RULE_STATE_FILTERS: RuleStateFilter[] = ['all', 'firing', 'ok', 'disabled', 'muted'];
export const RULE_KIND_FILTERS: AlertConditionKind[] = ['LogCount', 'MetricThreshold', 'ExceptionCount', 'Anomaly'];
const SORT_KEYS: RuleSortKey[] = ['name', 'state', 'lastFired'];

/** A rule's single display state: disabled wins, then firing, then ok. Muted is a separate overlay (see `isMuted`). */
export type RuleState = 'firing' | 'ok' | 'disabled';

export function ruleState(rule: AlertRule, status: AlertRuleStatus | undefined): RuleState {
	if (!rule.enabled) return 'disabled';
	return status?.firing ? 'firing' : 'ok';
}

// Firing first when sorting ascending, so the rules needing attention surface at the top.
const STATE_ORDER: Record<RuleState, number> = { firing: 0, ok: 1, disabled: 2 };

export function parseAlertListView(search: URLSearchParams): AlertListView {
	const state = search.get('state') as RuleStateFilter | null;
	const kind = search.get('kind') as AlertConditionKind | null;
	const sort = search.get('sort') as RuleSortKey | null;
	return {
		query: search.get('q') ?? '',
		label: search.get('label') ?? '',
		state: state && RULE_STATE_FILTERS.includes(state) ? state : 'all',
		kind: kind && RULE_KIND_FILTERS.includes(kind) ? kind : 'all',
		sort: sort && SORT_KEYS.includes(sort) ? sort : 'name',
		direction: search.get('dir') === 'desc' ? 'desc' : 'asc'
	};
}

/** Writes the view's non-default fields onto a copy of `base`, leaving unrelated params (e.g. `?rule=`) alone. */
export function buildAlertListSearch(base: URLSearchParams, view: AlertListView): string {
	const next = new URLSearchParams(base);
	for (const key of ['q', 'label', 'state', 'kind', 'sort', 'dir']) next.delete(key);
	if (view.query.trim()) next.set('q', view.query.trim());
	if (view.label.trim()) next.set('label', view.label.trim());
	if (view.state !== 'all') next.set('state', view.state);
	if (view.kind !== 'all') next.set('kind', view.kind);
	if (view.sort !== 'name') next.set('sort', view.sort);
	if (view.direction === 'desc') next.set('dir', 'desc');
	const s = next.toString();
	return s ? `?${s}` : '';
}

export function isAlertListViewActive(view: AlertListView): boolean {
	return view.query.trim() !== '' || view.label.trim() !== '' || view.state !== 'all' || view.kind !== 'all';
}

export function applyAlertListView(
	rules: AlertRule[],
	statuses: ReadonlyMap<string, AlertRuleStatus>,
	isMuted: (rule: AlertRule) => boolean,
	view: AlertListView
): AlertRule[] {
	const q = view.query.trim().toLowerCase();
	const labelFilter = parseLabels(view.label).labels;
	const filtered = rules.filter((rule) => {
		if (q && !rule.name.toLowerCase().includes(q) && !rule.description?.toLowerCase().includes(q)) return false;
		if (view.kind !== 'all' && rule.conditionKind !== view.kind) return false;
		if (Object.keys(labelFilter).length > 0 && !labelsMatch(labelFilter, rule.labels)) return false;
		if (view.state === 'muted') return rule.enabled && isMuted(rule);
		if (view.state !== 'all') return ruleState(rule, statuses.get(rule.id)) === view.state;
		return true;
	});

	const sign = view.direction === 'desc' ? -1 : 1;
	const byName = (a: AlertRule, b: AlertRule) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });
	const lastFired = (r: AlertRule) => {
		const at = statuses.get(r.id)?.lastFiredAt;
		return at ? Date.parse(at) : null;
	};

	return filtered.sort((a, b) => {
		let cmp = 0;
		if (view.sort === 'state') {
			cmp = STATE_ORDER[ruleState(a, statuses.get(a.id))] - STATE_ORDER[ruleState(b, statuses.get(b.id))];
		} else if (view.sort === 'lastFired') {
			const la = lastFired(a);
			const lb = lastFired(b);
			// Never-fired rules sort after every fired one whichever way the column is sorted.
			if (la === null || lb === null) return la === lb ? byName(a, b) : la === null ? 1 : -1;
			cmp = la - lb;
		}
		return cmp !== 0 ? sign * cmp : byName(a, b);
	});
}
