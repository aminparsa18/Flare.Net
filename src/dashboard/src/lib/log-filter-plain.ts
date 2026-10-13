// Conversions between the generated MemoryPack `LogFilter` wire class and `#lib/api.ts`'s plain
// `LogFilter` interface - the shape every Svelte component and `-api.ts` module works with.
// Hand-written because they map between two different TypeScript shapes (enums as strings,
// optional vs. nullable fields, `Date` vs. ISO string), which no serializer generator emits.

import { LogFilter } from '#lib/generated/memorypack/LogFilter.ts';
import { AttributeFilter } from '#lib/generated/memorypack/AttributeFilter.ts';
import { BodyJsonFilter } from '#lib/generated/memorypack/BodyJsonFilter.ts';
import {
	attributeBagFromString,
	attributeBagToString,
	attributeFilterOperatorFromString,
	attributeFilterOperatorToString,
	bodyJsonFilterOperatorFromString,
	bodyJsonFilterOperatorToString
} from '#lib/memorypack/enums.ts';
import { toGeneratedSpanFilter } from '#lib/traces-api.ts';
import type { LogFilter as PlainLogFilter } from '#lib/api.ts';

/** Converts the generated wire class to `#lib/api.ts`'s plain `LogFilter` interface - the shape every existing consumer (Svelte components, other `-api.ts` modules) already uses. */
export function logFilterToPlain(dto: LogFilter): PlainLogFilter {
	return {
		from: dto.from?.toISOString(),
		to: dto.to?.toISOString(),
		services: dto.services == null ? undefined : dto.services.map((s) => s ?? ''),
		severityNumbers: dto.severityNumbers ?? undefined,
		traceId: dto.traceId ?? undefined,
		spanId: dto.spanId ?? undefined,
		patternId: dto.patternId ?? undefined,
		search: dto.search ?? undefined,
		attributes:
			dto.attributes == null
				? undefined
				: dto.attributes.map((a) => ({
						bag: attributeBagToString(a!.bag),
						key: a!.key ?? '',
						value: a!.value ?? '',
						operator: attributeFilterOperatorToString(a!.operator),
						values: a!.values == null ? undefined : a!.values.map((v) => v ?? '')
					})),
		bodyJsonFilters:
			dto.bodyJsonFilters == null
				? undefined
				: dto.bodyJsonFilters.map((f) => ({
						path: f!.path ?? '',
						value: f!.value ?? '',
						operator: bodyJsonFilterOperatorToString(f!.operator),
						values: f!.values == null ? undefined : f!.values.map((v) => v ?? '')
					})),
		scopeNames: dto.scopeNames == null ? undefined : dto.scopeNames.map((s) => s ?? ''),
		// Only the fields the Logs Explorer's "Trace spans" popover writes (and that every
		// other client can express without a structure/attribute editor) round-trip here.
		traceSpanFilter:
			dto.traceSpanFilter == null
				? undefined
				: {
						services: dto.traceSpanFilter.services == null ? undefined : dto.traceSpanFilter.services.map((s) => s ?? ''),
						statusCodes:
							dto.traceSpanFilter.statusCodes == null ? undefined : dto.traceSpanFilter.statusCodes.map((s) => s ?? ''),
						names: dto.traceSpanFilter.names == null ? undefined : dto.traceSpanFilter.names.map((s) => s ?? ''),
						minDurationNano:
							dto.traceSpanFilter.minDurationNano == null ? undefined : Number(dto.traceSpanFilter.minDurationNano),
						maxDurationNano:
							dto.traceSpanFilter.maxDurationNano == null ? undefined : Number(dto.traceSpanFilter.maxDurationNano)
					}
		,
		searchAllFields: dto.searchAllFields || undefined
	};
}

/** Converts `#lib/api.ts`'s plain `LogFilter` interface to the generated wire class, for encoding a request. */
export function logFilterFromPlain(filter: PlainLogFilter | undefined): LogFilter {
	const dto = new LogFilter();
	if (filter == null) return dto;
	dto.from = filter.from == null ? null : new Date(filter.from);
	dto.to = filter.to == null ? null : new Date(filter.to);
	dto.services = filter.services ?? null;
	dto.severityNumbers = filter.severityNumbers ?? null;
	dto.traceId = filter.traceId ?? null;
	dto.spanId = filter.spanId ?? null;
	dto.patternId = filter.patternId ?? null;
	dto.search = filter.search ?? null;
	dto.attributes =
		filter.attributes == null
			? null
			: filter.attributes.map((a) => {
					const attr = new AttributeFilter();
					attr.bag = attributeBagFromString(a.bag);
					attr.key = a.key;
					attr.value = a.value;
					attr.operator = attributeFilterOperatorFromString(a.operator ?? 'Equals');
					attr.values = a.values ?? null;
					return attr;
				});
	dto.bodyJsonFilters =
		filter.bodyJsonFilters == null
			? null
			: filter.bodyJsonFilters.map((f) => {
					const jsonFilter = new BodyJsonFilter();
					jsonFilter.path = f.path;
					jsonFilter.value = f.value;
					jsonFilter.operator = bodyJsonFilterOperatorFromString(f.operator ?? 'Equals');
					jsonFilter.values = f.values ?? null;
					return jsonFilter;
				});
	dto.scopeNames = filter.scopeNames ?? null;
	dto.traceSpanFilter = filter.traceSpanFilter == null ? null : toGeneratedSpanFilter(filter.traceSpanFilter);
	dto.searchAllFields = filter.searchAllFields ?? false;
	return dto;
}
