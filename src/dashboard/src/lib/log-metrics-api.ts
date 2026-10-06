// Client for Flare.Api's log-based metrics (`/api/log-metrics`, ADR-0140): a saved LogFilter
// plus group-by keys that Flare.Ingest counts into a delta sum at flush time.
//
// Plain JSON, not MemoryPack - same call `metric-attribute-rules-api.ts` makes for a handful of
// small config rows. The API negotiates on Accept, so a request without the MemoryPack headers
// gets camelCase JSON. `condition` is the shared `LogFilter` wire shape from `$lib/api`.

import { API_BASE_URL, apiFetch, type LogFilter } from './api';

export interface LogMetric {
	id: string;
	name: string;
	description: string;
	enabled: boolean;
	/** The emitted metric's name, e.g. `logs.checkout.errors`. */
	metricName: string;
	condition: LogFilter;
	/** Attribute keys that become data-point attributes (series dimensions). */
	groupBy: string[];
	createdAt: string;
	updatedAt: string;
}

export interface LogMetricInput {
	name: string;
	description?: string;
	enabled?: boolean;
	metricName: string;
	condition: LogFilter;
	groupBy: string[];
}

/** Mirrors `LogMetricRequest.MaxGroupByKeys`. */
export const MAX_GROUP_BY_KEYS = 5;

export interface LogMetricPreviewSeries {
	serviceName: string;
	/** Aligned with the request's `groupBy` keys; an empty string means the log had no value. */
	values: string[];
	count: number;
}

export interface LogMetricPreview {
	windowMinutes: number;
	totalLogs: number;
	seriesCount: number;
	/** True when `seriesCount` hit the server's read cap, i.e. "at least this many". */
	seriesCapped: boolean;
	top: LogMetricPreviewSeries[];
}

const BASE = `${API_BASE_URL}/api/log-metrics`;

async function failure(res: Response, what: string): Promise<Error> {
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// Not a problem-details body - fall back to the status line.
	}
	return new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

const JSON_HEADERS = { 'Content-Type': 'application/json' };

export async function listLogMetrics(signal?: AbortSignal): Promise<LogMetric[]> {
	const res = await apiFetch(BASE, { signal });
	if (!res.ok) throw await failure(res, 'GET /api/log-metrics');
	return ((await res.json()) as { metrics: LogMetric[] }).metrics;
}

export async function createLogMetric(input: LogMetricInput): Promise<LogMetric> {
	const res = await apiFetch(BASE, { method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(input) });
	if (!res.ok) throw await failure(res, 'POST /api/log-metrics');
	return (await res.json()) as LogMetric;
}

export async function updateLogMetric(id: string, input: LogMetricInput): Promise<LogMetric> {
	const res = await apiFetch(`${BASE}/${id}`, { method: 'PUT', headers: JSON_HEADERS, body: JSON.stringify(input) });
	if (!res.ok) throw await failure(res, 'PUT /api/log-metrics');
	return (await res.json()) as LogMetric;
}

export async function deleteLogMetric(id: string): Promise<void> {
	const res = await apiFetch(`${BASE}/${id}`, { method: 'DELETE' });
	if (!res.ok) throw await failure(res, 'DELETE /api/log-metrics');
}

/** Dry-runs a draft over the last hour of stored logs - how many series its group-by keys would create. Writes nothing. */
export async function previewLogMetric(input: { condition: LogFilter; groupBy: string[] }): Promise<LogMetricPreview> {
	const res = await apiFetch(`${BASE}/preview`, { method: 'POST', headers: JSON_HEADERS, body: JSON.stringify(input) });
	if (!res.ok) throw await failure(res, 'POST /api/log-metrics/preview');
	return (await res.json()) as LogMetricPreview;
}

/** The query param `/settings/log-metrics` reads to open the create form pre-filled - see `buildLogMetricDraftHref`. */
export const LOG_METRIC_DRAFT_PARAM = 'filter';

/** Link to the create form carrying `filter` over (Logs explorer's "Create metric"). */
export function buildLogMetricDraftHref(filter: LogFilter, base: (path: string) => string): string {
	const params = new URLSearchParams({ new: '1', [LOG_METRIC_DRAFT_PARAM]: JSON.stringify(filter) });
	return base(`/settings/log-metrics?${params.toString()}`);
}

/** Parses the draft filter out of the page URL; null when absent or malformed. */
export function parseLogMetricDraft(url: URL): LogFilter | null {
	if (url.searchParams.get('new') !== '1') return null;
	const raw = url.searchParams.get(LOG_METRIC_DRAFT_PARAM);
	if (!raw) return {};
	try {
		const parsed = JSON.parse(raw);
		return parsed && typeof parsed === 'object' ? (parsed as LogFilter) : {};
	} catch {
		return {};
	}
}
