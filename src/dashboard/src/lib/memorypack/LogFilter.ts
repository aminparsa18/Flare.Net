// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogFilter.cs`'s `LogFilter` field-for-field, in
// declared order. Can't carry `[GenerateTypeScript]` itself because `From`/`To` are
// `DateTimeOffset?` - see `$lib/memorypack/date-time-offset.ts`'s header comment. Shared by
// every client file that reuses `LogFilter` as a nested member (`alerts-api.ts`'s
// `AlertRule.condition`, `metrics-api.ts`'s equivalent `MetricFilter`, `api.ts`'s own Logs
// endpoints), same "one filter DSL, several consumers" shape the JSON-era types had.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readNullableDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { AttributeFilter } from '$lib/memorypack/AttributeFilter';
import { attributeBagFromString, attributeBagToString } from '$lib/memorypack/enums';
import type { LogFilter as PlainLogFilter } from '$lib/api';

export class LogFilter {
	from: Date | null;
	to: Date | null;
	services: (string | null)[] | null;
	severityNumbers: number[] | null;
	traceId: string | null;
	spanId: string | null;
	patternId: string | null;
	search: string | null;
	attributes: (AttributeFilter | null)[] | null;

	constructor() {
		this.from = null;
		this.to = null;
		this.services = null;
		this.severityNumbers = null;
		this.traceId = null;
		this.spanId = null;
		this.patternId = null;
		this.search = null;
		this.attributes = null;
	}

	static serialize(value: LogFilter | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogFilter | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(9);
		writeNullableDateTimeOffset(writer, value.from);
		writeNullableDateTimeOffset(writer, value.to);
		writer.writeArray(value.services, (writer, x) => writer.writeString(x));
		writer.writeArray(value.severityNumbers, (writer, x) => writer.writeUint8(x));
		writer.writeString(value.traceId);
		writer.writeString(value.spanId);
		writer.writeString(value.patternId);
		writer.writeString(value.search);
		writer.writeArray(value.attributes, (writer, x) => AttributeFilter.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogFilter | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogFilter | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogFilter();
		if (count == 9) {
			value.from = readNullableDateTimeOffset(reader);
			value.to = readNullableDateTimeOffset(reader);
			value.services = reader.readArray((reader) => reader.readString());
			value.severityNumbers = reader.readArray((reader) => reader.readUint8());
			value.traceId = reader.readString();
			value.spanId = reader.readString();
			value.patternId = reader.readString();
			value.search = reader.readString();
			value.attributes = reader.readArray((reader) => AttributeFilter.deserializeCore(reader));
		} else if (count > 9) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.from = readNullableDateTimeOffset(reader);
			if (count == 1) return value;
			value.to = readNullableDateTimeOffset(reader);
			if (count == 2) return value;
			value.services = reader.readArray((reader) => reader.readString());
			if (count == 3) return value;
			value.severityNumbers = reader.readArray((reader) => reader.readUint8());
			if (count == 4) return value;
			value.traceId = reader.readString();
			if (count == 5) return value;
			value.spanId = reader.readString();
			if (count == 6) return value;
			value.patternId = reader.readString();
			if (count == 7) return value;
			value.search = reader.readString();
			if (count == 8) return value;
			value.attributes = reader.readArray((reader) => AttributeFilter.deserializeCore(reader));
			if (count == 9) return value;
		}
		return value;
	}
}

/** Converts this module's wire class to `$lib/api.ts`'s plain `LogFilter` interface - the shape every existing consumer (Svelte components, other `-api.ts` modules) already uses. */
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
						value: a!.value ?? ''
					}))
	};
}

/** Converts `$lib/api.ts`'s plain `LogFilter` interface to this module's wire class, for encoding a request. */
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
					return attr;
				});
	return dto;
}
