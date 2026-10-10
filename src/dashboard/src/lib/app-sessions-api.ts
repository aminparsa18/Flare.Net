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

export interface ReleaseHealthVersion {
	/** `service.version`, '' when the app reports none. */
	version: string;
	sessions: number;
	crashedSessions: number;
	erroredSessions: number;
	/** Distinct `user.id`; 0 when the app sets none. */
	users: number;
	crashedUsers: number;
}

export interface ReleaseHealth {
	windowMinutes: number;
	versions: ReleaseHealthVersion[];
}

/** Crash-free sessions and users per app version (ADR-0175). */
export async function getReleaseHealth(query: { windowMinutes: number; service?: string }, signal?: AbortSignal): Promise<ReleaseHealth> {
	const res = await apiFetch(`${API_BASE_URL}/api/app-sessions/release-health`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ windowMinutes: query.windowMinutes, service: query.service || null }),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/app-sessions/release-health failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as ReleaseHealth;
}

/** Share of `total` that did not crash, 0-100, or null when there is nothing to divide. */
export function crashFreePercent(crashed: number, total: number): number | null {
	return total > 0 ? ((total - crashed) / total) * 100 : null;
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
	breadcrumbCategory: string;
	breadcrumbMessage: string;
	/** An error screenshot was stored for this span (ADR-0174). */
	hasScreenshot: boolean;
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

export interface AppSessionScreenshot {
	contentType: string;
	imageBase64: string;
}

/** The screenshot stored for one span, or null when there is none (404). */
export async function getAppSessionScreenshot(
	sessionId: string,
	spanId: string,
	range: { fromUnixMs?: number; toUnixMs?: number },
	signal?: AbortSignal
): Promise<AppSessionScreenshot | null> {
	const res = await apiFetch(`${API_BASE_URL}/api/app-sessions/screenshot`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ sessionId, spanId, fromUnixMs: range.fromUnixMs ?? null, toUnixMs: range.toUnixMs ?? null }),
		signal
	});
	if (res.status === 404) return null;
	if (!res.ok) {
		throw new Error(`POST /api/app-sessions/screenshot failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as AppSessionScreenshot;
}
