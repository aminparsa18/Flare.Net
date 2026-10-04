// Kubernetes Events tab helpers. Events arrive as ordinary OTLP logs from the collector's
// `k8sobjects` receiver in `watch` mode, whose body is `{"type":"ADDED","object":{...Event...}}`
// (the OTLP kvlist body is stored as JSON - see json-body.ts). No dedicated storage: the tab is a
// filtered logs search, and these pure helpers build that filter and read a row back out.

import type { BodyJsonFilter, LogEventDto, LogFilter } from '$lib/api';
import { parseJsonBody, extractBodyPath } from '$lib/logs/json-body';

export const KUBERNETES_EVENT_TYPES = ['Warning', 'Normal'] as const;
export type KubernetesEventType = (typeof KUBERNETES_EVENT_TYPES)[number];

const OBJECT = 'object';

export interface KubernetesEventFilters {
	/** Empty = Warning and Normal. */
	type: string;
	/** Empty = all namespaces. */
	namespace: string;
	/** The involved object's kind (Pod, Node, ...) - empty = any. */
	kind: string;
	/** The involved object's name - empty = any. */
	name: string;
	/** Free text matched against the event body (reason/message). */
	search: string;
}

export const EMPTY_EVENT_FILTERS: KubernetesEventFilters = { type: '', namespace: '', kind: '', name: '', search: '' };

export interface KubernetesEvent {
	eventId: string;
	timestamp: string;
	type: string;
	reason: string;
	message: string;
	kind: string;
	name: string;
	namespace: string;
	/** How many times the event repeated, when the API server aggregated it. */
	count: number | null;
}

/** The logs filter for the Events tab; `fromIso` is the window start. Requires `involvedObject.kind` so only Event objects match. */
export function buildKubernetesEventsFilter(filters: KubernetesEventFilters, fromIso: string): LogFilter {
	const bodyJsonFilters: BodyJsonFilter[] = [{ path: `${OBJECT}.involvedObject.kind`, operator: 'Exists', value: '' }];
	const equals = (path: string, value: string) => {
		if (value) bodyJsonFilters.push({ path: `${OBJECT}.${path}`, operator: 'Equals', value });
	};
	equals('type', filters.type);
	equals('involvedObject.namespace', filters.namespace);
	equals('involvedObject.kind', filters.kind);
	equals('involvedObject.name', filters.name);
	return { from: fromIso, bodyJsonFilters, search: filters.search || undefined };
}

/** Reads a log row as an event, or null when its body isn't an Event object. */
export function parseKubernetesEvent(log: LogEventDto): KubernetesEvent | null {
	const parsed = parseJsonBody(log.body);
	const kind = extractBodyPath(parsed, `${OBJECT}.involvedObject.kind`);
	if (!kind) return null;
	const read = (path: string) => extractBodyPath(parsed, `${OBJECT}.${path}`);
	const count = Number(read('count') || read('series.count'));
	return {
		eventId: log.eventId,
		timestamp: log.timestamp,
		type: read('type'),
		reason: read('reason'),
		message: read('message'),
		kind,
		name: read('involvedObject.name'),
		namespace: read('involvedObject.namespace'),
		count: Number.isFinite(count) && count > 0 ? count : null
	};
}
