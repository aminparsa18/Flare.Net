// Client for Flare.Api's metric attribute-reduction rules (`/api/metric-attribute-rules`,
// ADR-0083): per-metric drop / keep-only lists of data-point attributes applied at ingest.
//
// Plain JSON, not MemoryPack - a handful of small config rows managed from one panel, so the
// generated-TS/hand-written-codec cost isn't worth it. The API negotiates on Accept, so a
// request without the MemoryPack headers gets camelCase JSON with string enums.

import { API_BASE_URL, apiFetch } from './api';

export type MetricAttributeRuleMode = 'Drop' | 'KeepOnly';

export interface MetricAttributeRule {
	id: string;
	name: string;
	description: string;
	enabled: boolean;
	/** Exact metric name, or a prefix with one trailing `*`. */
	metricName: string;
	mode: MetricAttributeRuleMode;
	attributes: string[];
}

export interface MetricAttributeRuleInput {
	name: string;
	description?: string;
	enabled?: boolean;
	metricName: string;
	mode: MetricAttributeRuleMode;
	attributes: string[];
}

const BASE = `${API_BASE_URL}/api/metric-attribute-rules`;

async function failure(res: Response, what: string): Promise<Error> {
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// Not a problem-details body - fall back to the status line.
	}
	return new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

export async function listMetricAttributeRules(): Promise<MetricAttributeRule[]> {
	const res = await apiFetch(BASE);
	if (!res.ok) throw await failure(res, 'GET /api/metric-attribute-rules');
	return ((await res.json()) as { rules: MetricAttributeRule[] }).rules;
}

export async function createMetricAttributeRule(input: MetricAttributeRuleInput): Promise<MetricAttributeRule> {
	const res = await apiFetch(BASE, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(input)
	});
	if (!res.ok) throw await failure(res, 'POST /api/metric-attribute-rules');
	return (await res.json()) as MetricAttributeRule;
}

export async function updateMetricAttributeRule(id: string, input: MetricAttributeRuleInput): Promise<MetricAttributeRule> {
	const res = await apiFetch(`${BASE}/${id}`, {
		method: 'PUT',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(input)
	});
	if (!res.ok) throw await failure(res, 'PUT /api/metric-attribute-rules');
	return (await res.json()) as MetricAttributeRule;
}

export async function deleteMetricAttributeRule(id: string): Promise<void> {
	const res = await apiFetch(`${BASE}/${id}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, 'DELETE /api/metric-attribute-rules');
}

/** Whether a rule's metric name (exact, or prefix + `*`) covers `metricName` - mirrors `MetricAttributeReducer.Matches`. */
export function ruleMatchesMetric(rule: MetricAttributeRule, metricName: string): boolean {
	return rule.metricName.endsWith('*') ? metricName.startsWith(rule.metricName.slice(0, -1)) : rule.metricName === metricName;
}
