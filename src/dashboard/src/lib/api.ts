// Client for Flare.Api's Query API (search/aggregate) and live-tail WebSocket.
// Field names/casing/enum values are a hand-mirror of src/Flare.Api/Model/*.cs +
// Json/LogsJsonContext.cs / LogTailJsonContext.cs (camelCase properties, PascalCase
// string enums - `UseStringEnumConverter` without a naming policy leaves enum member
// names as-is). Keep in sync with those files, same convention Flare.Api's own DTOs
// use against the ClickHouse DDL.

import { env } from '$env/dynamic/public';

/** Falls back to Flare.Api's fixed standalone dev port (see src/Flare.Api/Properties/launchSettings.json). */
export const API_BASE_URL = env.PUBLIC_API_URL ?? 'http://localhost:5085';

/**
 * `fetch` wrapper every `lib/*-api.ts` module calls instead of the global `fetch` -
 * `credentials: 'include'` is required for the session cookie (see
 * `lib/auth/state.svelte.ts`) to actually be sent, since the dashboard and Flare.Api are
 * different origins even in docker-compose (different ports). Same signature as `fetch`
 * itself, so every existing call site's URL-building/options stay exactly as they were -
 * only the function name changes.
 */
export async function apiFetch(input: string, init: RequestInit = {}): Promise<Response> {
	return fetch(input, { ...init, credentials: 'include' });
}

// ---- Shared filter shape (LogFilter.cs) ----------------------------------

export type AttributeBag = 'Log' | 'Resource' | 'Scope';

export interface AttributeFilter {
	bag: AttributeBag;
	key: string;
	value: string;
}

export interface LogFilter {
	from?: string;
	to?: string;
	services?: string[];
	severityNumbers?: number[];
	traceId?: string;
	spanId?: string;
	patternId?: string;
	search?: string;
	attributes?: AttributeFilter[];
}

// ---- Log event DTO (LogEventDto.cs) ---------------------------------------

export interface LogEventDto {
	eventId: string;
	timestamp: string;
	observedTimestamp: string;
	traceId: string;
	spanId: string;
	traceFlags: number;
	severityText: string;
	severityNumber: number;
	serviceName: string;
	body: string;
	resourceSchemaUrl: string;
	resourceAttributes: Record<string, string>;
	scopeSchemaUrl: string;
	scopeName: string;
	scopeVersion: string;
	scopeAttributes: Record<string, string>;
	logAttributes: Record<string, string>;
	eventName: string;
	patternId: string;
	patternTemplate: string;
	/**
	 * Enclosing span's duration in nanoseconds. `System.Text.Json` has no
	 * `DefaultIgnoreCondition` configured for this context, so an unset `ulong?` on the
	 * backend serializes as an explicit JSON `null`, not an omitted key - `null`, not
	 * just `undefined`, so callers must check `!= null`, never `!== undefined` alone.
	 * Also `null` for every live-tailed event regardless of `includeSpanDuration`
	 * (live-tail rows never go through the backend's follow-up query - see
	 * LogQueryService.SearchAsync's remarks).
	 */
	spanDurationNano?: number | null;
}

// ---- POST /api/logs/search (LogSearchRequest.cs / LogSearchResponse) ------

export interface LogSearchRequest {
	filter?: LogFilter;
	cursor?: string;
	pageSize?: number;
	/** Opt-in: populate each event's `spanDurationNano` via a follow-up query. See LogSearchRequest.cs's remarks. */
	includeSpanDuration?: boolean;
}

export interface LogSearchResponse {
	events: LogEventDto[];
	nextCursor: string | null;
}

export async function searchLogs(request: LogSearchRequest = {}, signal?: AbortSignal): Promise<LogSearchResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/logs/search`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/logs/search failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

// ---- POST /api/logs/aggregate (LogAggregateRequest.cs / LogAggregateResponse) ----

export type LogAggregateGroupBy = 'None' | 'Service' | 'Level';

export interface LogAggregateRequest {
	filter?: LogFilter;
	bucketWidthSeconds: number;
	groupBy?: LogAggregateGroupBy;
}

export interface LogAggregateBucket {
	bucketStart: string;
	groupKey: string | null;
	count: number;
}

export interface LogAggregateResponse {
	buckets: LogAggregateBucket[];
}

export async function aggregateLogs(request: LogAggregateRequest, signal?: AbortSignal): Promise<LogAggregateResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/logs/aggregate`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/logs/aggregate failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

// ---- POST /api/logs/numeric-attribute-keys, POST /api/logs/value-distribution ---------
// (LogValueDistributionRequest.cs) - the Logs page's "Value distribution" chart: pick any
// numeric LogAttributes key (there's no first-class duration field - see Planning.md's
// "no duration/p95 in v1" rationale on the Patterns feature for why that's deliberate) and
// see its values scatter-plotted over time. The key list and the sampled points are two
// separate calls so switching the picker doesn't re-run attribute discovery.

export interface LogAttributeKeysRequest {
	filter?: LogFilter;
}

export interface LogAttributeKeyInfo {
	key: string;
	numericCount: number;
}

export interface LogAttributeKeysResponse {
	keys: LogAttributeKeyInfo[];
}

export async function getLogAttributeKeys(
	request: LogAttributeKeysRequest = {},
	signal?: AbortSignal
): Promise<LogAttributeKeysResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/logs/numeric-attribute-keys`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/logs/numeric-attribute-keys failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

export interface LogValueDistributionRequest {
	filter?: LogFilter;
	attributeKey: string;
	sampleSize?: number;
}

export interface LogValueDistributionPoint {
	timestamp: string;
	value: number;
}

export interface LogValueDistributionResponse {
	points: LogValueDistributionPoint[];
}

export async function getLogValueDistribution(
	request: LogValueDistributionRequest,
	signal?: AbortSignal
): Promise<LogValueDistributionResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/logs/value-distribution`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/logs/value-distribution failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

// ---- POST /api/logs/patterns (LogPatternModels.cs) -------------------------
//
// Ranked Drain-cluster ("log pattern detection") view - "GET /api/orders/123" and
// "GET /api/orders/456" collapse into one template row, ranked by occurrence count. No
// duration/p95 (logs have no duration field - see Planning.md's rationale); pairs with
// `LogFilter.patternId` for drilling from a pattern row into the Logs Explorer.

export interface LogPatternRequest {
	filter?: LogFilter;
	topN?: number;
}

export interface LogPatternRow {
	patternId: string;
	template: string;
	count: number;
	errorCount: number;
	firstSeen: string;
	lastSeen: string;
}

export interface LogPatternResponse {
	patterns: LogPatternRow[];
}

export async function getPatterns(request: LogPatternRequest = {}, signal?: AbortSignal): Promise<LogPatternResponse> {
	const res = await apiFetch(`${API_BASE_URL}/api/logs/patterns`, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json' },
		body: JSON.stringify(request),
		signal
	});
	if (!res.ok) {
		throw new Error(`POST /api/logs/patterns failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

// ---- GET /api/logs/tail (WebSocket, LogTailMessages.cs) -------------------
//
// Enum values on the wire are PascalCase (the C# member name as-is) - LogsJsonContext/
// LogTailJsonContext's `UseStringEnumConverter` has no naming policy applied to it, only
// `PropertyNamingPolicy` (which affects property names like "type"/"filter", not enum
// values). Confirmed against a real running connection, not assumed - the deserializer on
// the *client message* side is case-insensitive so a lowercase 'subscribe' is silently
// accepted, but the *server message* side always writes the canonical form, so getting
// this wrong here only breaks receiving, not sending - worth the explicit note since
// that asymmetry makes the bug easy to reintroduce and hard to notice from the client-to-server side alone.

export type LogTailClientMessage =
	| { type: 'Subscribe'; filter: LogFilter }
	| { type: 'Pause' }
	| { type: 'Resume' };

export type LogTailServerMessage =
	| { type: 'Event'; event: LogEventDto }
	| { type: 'Dropped'; droppedCount: number }
	| { type: 'Error'; error: string };

export type LiveTailStatus = 'connecting' | 'open' | 'closed' | 'error';

export interface LiveTailHandlers {
	onStatusChange?: (status: LiveTailStatus) => void;
	onEvent?: (event: LogEventDto) => void;
	onDropped?: (droppedCount: number) => void;
	onError?: (error: string) => void;
}

/** Handle returned by {@link connectLiveTail} - lets a caller re-subscribe with a new filter or pause/resume without reconnecting the socket. */
export interface LiveTailConnection {
	/** (Re)subscribes, replacing any previous filter. Queued if the socket hasn't finished connecting yet. */
	subscribe(filter: LogFilter): void;
	pause(): void;
	resume(): void;
	close(): void;
}

/**
 * Opens the live-tail WebSocket and sends an initial `subscribe` once connected.
 * `pendingFilter` tracks the most recently requested filter so a `subscribe()` call that
 * arrives before the handshake completes isn't lost - the `open` handler always sends
 * whatever is current at that point, not just the filter passed in here.
 */
export function connectLiveTail(filter: LogFilter, handlers: LiveTailHandlers): LiveTailConnection {
	const wsUrl = `${API_BASE_URL.replace(/^http/, 'ws')}/api/logs/tail`;
	const socket = new WebSocket(wsUrl);
	let pendingFilter = filter;

	handlers.onStatusChange?.('connecting');

	function send(message: LogTailClientMessage) {
		if (socket.readyState === WebSocket.OPEN) {
			socket.send(JSON.stringify(message));
		}
	}

	socket.addEventListener('open', () => {
		handlers.onStatusChange?.('open');
		send({ type: 'Subscribe', filter: pendingFilter });
	});

	socket.addEventListener('message', (ev) => {
		const message: LogTailServerMessage = JSON.parse(ev.data);
		switch (message.type) {
			case 'Event':
				handlers.onEvent?.(message.event);
				break;
			case 'Dropped':
				handlers.onDropped?.(message.droppedCount);
				break;
			case 'Error':
				handlers.onError?.(message.error);
				break;
		}
	});

	socket.addEventListener('close', () => handlers.onStatusChange?.('closed'));
	socket.addEventListener('error', () => handlers.onStatusChange?.('error'));

	return {
		subscribe(newFilter) {
			pendingFilter = newFilter;
			send({ type: 'Subscribe', filter: newFilter });
		},
		pause() {
			send({ type: 'Pause' });
		},
		resume() {
			send({ type: 'Resume' });
		},
		close() {
			socket.close();
		}
	};
}

// ---- GET /api/resources/snapshot + GET /api/resources/watch (ResourceGraphDto.cs) ----
//
// Docker-driven Resources page - see docs/standalone.md#resources-page-optional-docker-access.
// `available: false` means the feature isn't configured (no DockerResources:ProxyUrl on
// Flare.Api's side), not "no AppHost" - a different, simpler story than LiveTail-adjacent
// features get elsewhere in this file. `state`/`health` are PascalCase string enum values,
// same UseStringEnumConverter asymmetry documented above for LogTailServerMessage.

export type ResourceState = 'Unknown' | 'Running' | 'Exited' | 'Restarting' | 'Paused';
export type ResourceHealth = 'Starting' | 'Healthy' | 'Unhealthy';

export interface ResourceNodeDto {
	id: string;
	role: string;
	name: string;
	image: string;
	state: ResourceState;
	health: ResourceHealth | null;
	urls: string[];
}

export interface ResourceEdgeDto {
	/** Despite the name, any graph node id - a Docker `flare.role` value (`ResourceNodeDto.role`) or a producer's `ProducerServiceDto.id`. See `ResourceEdgeDto.cs`'s remarks. */
	sourceRole: string;
	targetRole: string;
	relationshipType: string;
}

/** A service observed sending telemetry into `ingest` recently - sourced from ClickHouse, not Docker, so it can represent a producer with no Docker footprint at all (e.g. an `AddProject` resource under Aspire's dev-loop). See `ResourceGraphDto.cs`'s remarks. */
export interface ProducerServiceDto {
	id: string;
	serviceName: string;
	lastSeenAt: string;
}

export interface ResourceGraphSnapshot {
	available: boolean;
	unavailableReason: string | null;
	nodes: ResourceNodeDto[];
	edges: ResourceEdgeDto[];
	producers: ProducerServiceDto[];
	updatedAt: string | null;
}

export async function fetchResourceSnapshot(signal?: AbortSignal): Promise<ResourceGraphSnapshot> {
	const res = await apiFetch(`${API_BASE_URL}/api/resources/snapshot`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/resources/snapshot failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

export type ResourcesWatchStatus = 'connecting' | 'open' | 'closed' | 'error';

export interface ResourcesWatchHandlers {
	onStatusChange?: (status: ResourcesWatchStatus) => void;
	onSnapshot?: (snapshot: ResourceGraphSnapshot) => void;
}

/** Handle returned by {@link connectResourcesWatch}. Push-only on the wire (no client->server messages, unlike {@link LiveTailConnection}) - the graph isn't filtered, so there's nothing to subscribe/pause/resume. */
export interface ResourcesWatchConnection {
	close(): void;
}

/**
 * Opens the resources-watch WebSocket - every message received is a complete, fresh
 * {@link ResourceGraphSnapshot} (not an incremental diff or a discriminated-union
 * envelope like {@link LogTailServerMessage}), pushed once per `DockerContainerPoller`
 * tick on the server plus immediately on connect.
 */
export function connectResourcesWatch(handlers: ResourcesWatchHandlers): ResourcesWatchConnection {
	const wsUrl = `${API_BASE_URL.replace(/^http/, 'ws')}/api/resources/watch`;
	const socket = new WebSocket(wsUrl);

	handlers.onStatusChange?.('connecting');

	socket.addEventListener('open', () => handlers.onStatusChange?.('open'));
	socket.addEventListener('message', (ev) => {
		const snapshot: ResourceGraphSnapshot = JSON.parse(ev.data);
		handlers.onSnapshot?.(snapshot);
	});
	socket.addEventListener('close', () => handlers.onStatusChange?.('closed'));
	socket.addEventListener('error', () => handlers.onStatusChange?.('error'));

	return {
		close() {
			socket.close();
		}
	};
}

// ---- Resources page: Host overview panel (HostStatsSnapshot.cs) ----------

/** Mirrors `Model/HostStatsSnapshot.cs` - see that file's own remarks for each field. */
export interface HostStatsSnapshot {
	available: boolean;
	unavailableReason: string | null;
	cpuUsagePercent: number;
	cpuCoreCount: number;
	loadAverage1m: number;
	memoryTotalBytes: number;
	memoryUsedBytes: number;
	diskTotalBytes: number;
	diskUsedBytes: number;
	uptimeSeconds: number;
	updatedAt: string | null;
}

export async function fetchHostStatsSnapshot(signal?: AbortSignal): Promise<HostStatsSnapshot> {
	const res = await apiFetch(`${API_BASE_URL}/api/resources/host/snapshot`, { signal });
	if (!res.ok) {
		throw new Error(`GET /api/resources/host/snapshot failed: ${res.status} ${res.statusText}`);
	}
	return res.json();
}

export type HostStatsWatchStatus = ResourcesWatchStatus;

export interface HostStatsWatchHandlers {
	onStatusChange?: (status: HostStatsWatchStatus) => void;
	onSnapshot?: (snapshot: HostStatsSnapshot) => void;
}

/** Handle returned by {@link connectHostStatsWatch}. Same push-only shape as {@link ResourcesWatchConnection}. */
export interface HostStatsWatchConnection {
	close(): void;
}

/**
 * Opens the host-stats-watch WebSocket - every message received is a complete, fresh
 * {@link HostStatsSnapshot} pushed once per `HostStatsPoller` tick on the server plus
 * immediately on connect. Same shape as {@link connectResourcesWatch}, kept as its own
 * function/connection since it's an independent stream (no shared subscribe/pause
 * protocol, no shared enablement with the Docker resource graph).
 */
export function connectHostStatsWatch(handlers: HostStatsWatchHandlers): HostStatsWatchConnection {
	const wsUrl = `${API_BASE_URL.replace(/^http/, 'ws')}/api/resources/host/watch`;
	const socket = new WebSocket(wsUrl);

	handlers.onStatusChange?.('connecting');

	socket.addEventListener('open', () => handlers.onStatusChange?.('open'));
	socket.addEventListener('message', (ev) => {
		const snapshot: HostStatsSnapshot = JSON.parse(ev.data);
		handlers.onSnapshot?.(snapshot);
	});
	socket.addEventListener('close', () => handlers.onStatusChange?.('closed'));
	socket.addEventListener('error', () => handlers.onStatusChange?.('error'));

	return {
		close() {
			socket.close();
		}
	};
}
