// The real consumer path: what `src/lib/api.ts` does with a decoded `LogEventDto` before any
// component sees it. `toLogEventDto` there is module-private and `api.ts` imports
// `$app/env/public` (SvelteKit-only, not resolvable under tsx), so the function is mirrored
// here verbatim - keep it in sync with `toLogEventDto` in `src/lib/api.ts`.

import type { LogEventDto as GeneratedLogEventDto } from '#lib/memorypack/LogEventDto.ts';

export interface PlainLogEvent {
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
	spanDurationNano: number | null;
}

export function toLogEventDto(dto: GeneratedLogEventDto): PlainLogEvent {
	return {
		eventId: dto.eventId,
		timestamp: dto.timestamp.toISOString(),
		observedTimestamp: dto.observedTimestamp.toISOString(),
		traceId: dto.traceId ?? '',
		spanId: dto.spanId ?? '',
		traceFlags: dto.traceFlags,
		severityText: dto.severityText ?? '',
		severityNumber: dto.severityNumber,
		serviceName: dto.serviceName ?? '',
		body: dto.body ?? '',
		resourceSchemaUrl: dto.resourceSchemaUrl ?? '',
		resourceAttributes: dto.resourceAttributes ?? {},
		scopeSchemaUrl: dto.scopeSchemaUrl ?? '',
		scopeName: dto.scopeName ?? '',
		scopeVersion: dto.scopeVersion ?? '',
		scopeAttributes: dto.scopeAttributes ?? {},
		logAttributes: dto.logAttributes ?? {},
		eventName: dto.eventName ?? '',
		patternId: dto.patternId ?? '',
		patternTemplate: dto.patternTemplate ?? '',
		spanDurationNano: dto.spanDurationNano == null ? null : Number(dto.spanDurationNano)
	};
}
