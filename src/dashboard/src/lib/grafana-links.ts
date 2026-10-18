// "Open in Grafana" (ADR-0248): the operator sets PUBLIC_GRAFANA_TRACE_URL to a Grafana Explore or
// Tempo URL with a {traceId} placeholder, e.g.
//   https://grafana.example.com/explore?left={"datasource":"tempo","queries":[{"query":"{traceId}"}]}
// Only http(s) templates are honoured, so a mistyped value can never become a javascript: link.

import { PUBLIC_GRAFANA_TRACE_URL } from '$app/env/public';

export function buildGrafanaTraceUrl(template: string | undefined, traceId: string): string | null {
	const trimmed = template?.trim();
	if (!trimmed || !trimmed.includes('{traceId}')) return null;
	const url = trimmed.replaceAll('{traceId}', encodeURIComponent(traceId));
	try {
		const { protocol } = new URL(url);
		return protocol === 'http:' || protocol === 'https:' ? url : null;
	} catch {
		return null;
	}
}

export const grafanaTraceUrl = (traceId: string) => buildGrafanaTraceUrl(PUBLIC_GRAFANA_TRACE_URL, traceId);
