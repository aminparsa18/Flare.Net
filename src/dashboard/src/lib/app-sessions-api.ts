// Client for Flare.Api's app-session list (`POST /api/app-sessions/list`, see
// src/Flare.Api/Endpoints/AppSessionEndpoints.cs): one row per client-app session, grouped from
// spans' `session.id`. Plain JSON, like `slos-api.ts`. Not the dashboard's own login sessions
// (`sessions-api.ts`). See docs-internal/adr/0167-app-sessions-view.md.

import { API_BASE_URL, apiFetch } from './api';

export interface AppSession {
	sessionId: string;
	serviceName: string;
	/** `service.version`, '' when the app reports none. */
	version: string;
	os: string;
	device: string;
	firstSeenUnixMs: number;
	lastSeenUnixMs: number;
	spanCount: number;
	traceCount: number;
	errorCount: number;
	screens: string[];
}

export interface AppSessionsResponse {
	windowMinutes: number;
	sessions: AppSession[];
	/** More sessions matched than the server returns. */
	truncated: boolean;
	/** Every service / version with sessions in the window, unaffected by the filters. */
	services: string[];
	versions: string[];
}

export interface AppSessionsQuery {
	windowMinutes: number;
	service?: string;
	version?: string;
	errorsOnly?: boolean;
}

export async function getAppSessions(query: AppSessionsQuery, signal?: AbortSignal): Promise<AppSessionsResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/app-sessions/list`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({
			windowMinutes: query.windowMinutes,
			service: query.service || null,
			version: query.version || null,
			errorsOnly: query.errorsOnly ?? false
		}),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/app-sessions/list failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as AppSessionsResponse;
}

export interface AppSessionTimelineEvent {
	traceId: string;
	spanId: string;
	name: string;
	serviceName: string;
	startUnixMs: number;
	durationMs: number;
	/** `screen.name`, '' when the span has none. */
	screen: string;
	isError: boolean;
	statusMessage: string;
	exceptionType: string;
	exceptionMessage: string;
}

export interface AppSessionTimeline {
	sessionId: string;
	/** Empty when the session has no spans in the window. */
	serviceName: string;
	version: string;
	os: string;
	device: string;
	events: AppSessionTimelineEvent[];
	/** The session has more spans than the server returns. */
	truncated: boolean;
}

export async function getAppSessionTimeline(
	sessionId: string,
	range: { fromUnixMs?: number; toUnixMs?: number },
	signal?: AbortSignal
): Promise<AppSessionTimeline> {
	const res = await apiFetch(`${API_BASE_URL}/api/app-sessions/timeline`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ sessionId, fromUnixMs: range.fromUnixMs ?? null, toUnixMs: range.toUnixMs ?? null }),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/app-sessions/timeline failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as AppSessionTimeline;
}
