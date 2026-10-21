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

export interface ReleaseHealthTrendPoint {
	/** Bucket start, Unix epoch milliseconds. */
	bucketUnixMs: number;
	version: string;
	sessions: number;
	crashedSessions: number;
}

export interface ReleaseHealthTrend {
	windowMinutes: number;
	bucketMinutes: number;
	points: ReleaseHealthTrendPoint[];
}

/** Sessions and crashed sessions per version per time bucket (ADR-0189). */
export async function getReleaseHealthTrend(query: { windowMinutes: number; service?: string }, signal?: AbortSignal): Promise<ReleaseHealthTrend> {
	const res = await apiFetch(`${API_BASE_URL}/api/app-sessions/release-health/trend`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ windowMinutes: query.windowMinutes, service: query.service || null }),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/app-sessions/release-health/trend failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as ReleaseHealthTrend;
}

export interface AppStartStat {
	/** `service.version`, '' when the app reports none. */
	version: string;
	type: 'cold' | 'warm' | string;
	count: number;
	p50Ms: number;
	p95Ms: number;
}

export interface ScreenPerformance {
	screen: string;
	loads: number;
	loadP50Ms: number;
	loadP95Ms: number;
	/** Screen visits that reported frames. */
	visits: number;
	frames: number;
	/** Includes the frozen ones. */
	slowFrames: number;
	frozenFrames: number;
}

export interface AppPerformance {
	windowMinutes: number;
	starts: AppStartStat[];
	screens: ScreenPerformance[];
}

/** App start times and per-screen load and frame health (ADR-0180). */
export async function getAppPerformance(
	query: { windowMinutes: number; service?: string; version?: string },
	signal?: AbortSignal
): Promise<AppPerformance> {
	const res = await apiFetch(`${API_BASE_URL}/api/app-sessions/performance`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ windowMinutes: query.windowMinutes, service: query.service || null, version: query.version || null }),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/app-sessions/performance failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as AppPerformance;
}

/** Share of frames that were slow (or frozen), 0-100, or null when no frames were reported. */
export function framePercent(count: number, frames: number): number | null {
	return frames > 0 ? (count / frames) * 100 : null;
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

/** The times of a session's scheduled screenshots (session replay-lite, ADR-0192), oldest first. */
export interface AppSessionReplay {
	sessionId: string;
	frameUnixMs: number[];
	truncated: boolean;
}

export async function getAppSessionReplay(
	sessionId: string,
	range: { fromUnixMs?: number; toUnixMs?: number },
	signal?: AbortSignal
): Promise<AppSessionReplay> {
	const res = await apiFetch(`${API_BASE_URL}/api/app-sessions/replay`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ sessionId, fromUnixMs: range.fromUnixMs ?? null, toUnixMs: range.toUnixMs ?? null }),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/app-sessions/replay failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as AppSessionReplay;
}

/** The scheduled screenshot taken at one of the session's frame times, or null when it is gone (404). */
export async function getAppSessionReplayFrame(
	sessionId: string,
	timeUnixMs: number,
	signal?: AbortSignal
): Promise<AppSessionScreenshot | null> {
	const res = await apiFetch(`${API_BASE_URL}/api/app-sessions/replay/frame`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ sessionId, timeUnixMs }),
		signal
	});
	if (res.status === 404) return null;
	if (!res.ok) {
		throw new Error(`POST /api/app-sessions/replay/frame failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as AppSessionScreenshot;
}

/** One batch of rrweb events from a browser session (browser session replay, ADR-0264). */
export interface WebReplayChunk {
	chunkId: string;
	startUnixMs: number;
	endUnixMs: number;
	eventCount: number;
	payloadBytes: number;
}

export interface WebReplay {
	sessionId: string;
	serviceName: string;
	chunks: WebReplayChunk[];
	truncated: boolean;
}

/** The recorded chunks of a browser session, oldest first; none when the session was not recorded. */
export async function getWebReplay(
	sessionId: string,
	range: { fromUnixMs?: number; toUnixMs?: number },
	signal?: AbortSignal
): Promise<WebReplay> {
	const res = await apiFetch(`${API_BASE_URL}/api/web-replay`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ sessionId, fromUnixMs: range.fromUnixMs ?? null, toUnixMs: range.toUnixMs ?? null }),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/web-replay failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as WebReplay;
}

/** One chunk's rrweb events, or null when it is gone (404). */
export async function getWebReplayChunk(
	sessionId: string,
	chunk: Pick<WebReplayChunk, 'chunkId' | 'startUnixMs'>,
	signal?: AbortSignal
): Promise<unknown[] | null> {
	const res = await apiFetch(`${API_BASE_URL}/api/web-replay/chunk`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ sessionId, chunkId: chunk.chunkId, startUnixMs: chunk.startUnixMs }),
		signal
	});
	if (res.status === 404) return null;
	if (!res.ok) {
		throw new Error(`POST /api/web-replay/chunk failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as unknown[];
}

export interface AppFeedback {
	feedbackId: string;
	timeUnixMs: number;
	serviceName: string;
	sessionId: string;
	userId: string;
	name: string;
	email: string;
	appVersion: string;
	message: string;
}

export interface AppFeedbackResponse {
	items: AppFeedback[];
	/** More messages matched than the server returns (the oldest were cut). */
	truncated: boolean;
}

export interface AppFeedbackQuery {
	windowMinutes: number;
	service?: string;
}

/** User feedback sent from the app (`POST /api/app-feedback/list`, ADR-0193), newest first. */
export async function getAppFeedback(query: AppFeedbackQuery, signal?: AbortSignal): Promise<AppFeedbackResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/app-feedback/list`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify({ windowMinutes: query.windowMinutes, service: query.service || null }),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/app-feedback/list failed: ${res.status} ${res.statusText}`);
	}
	return (await res.json()) as AppFeedbackResponse;
}
