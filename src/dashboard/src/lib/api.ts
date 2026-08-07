// Client for Flare.Api's Query API (search/aggregate) and live-tail WebSocket.
// Field names/casing/enum values are a hand-mirror of src/Flare.Api/Model/*.cs +
// Json/LogsJsonContext.cs / LogTailJsonContext.cs (camelCase properties, PascalCase
// string enums - `UseStringEnumConverter` without a naming policy leaves enum member
// names as-is). Keep in sync with those files, same convention Flare.Api's own DTOs
// use against the ClickHouse DDL.

import { env } from '$env/dynamic/public';

/** Falls back to Flare.Api's fixed standalone dev port (see src/Flare.Api/Properties/launchSettings.json). */
export const API_BASE_URL = env.PUBLIC_API_URL ?? 'http://localhost:5085';

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
}

// ---- POST /api/logs/search (LogSearchRequest.cs / LogSearchResponse) ------

export interface LogSearchRequest {
	filter?: LogFilter;
	cursor?: string;
	pageSize?: number;
}

export interface LogSearchResponse {
	events: LogEventDto[];
	nextCursor: string | null;
}

export async function searchLogs(request: LogSearchRequest = {}, signal?: AbortSignal): Promise<LogSearchResponse> {
	const res = await fetch(`${API_BASE_URL}/api/logs/search`, {
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
	const res = await fetch(`${API_BASE_URL}/api/logs/aggregate`, {
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
