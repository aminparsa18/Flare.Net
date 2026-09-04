// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanFilter.cs`'s `SpanFilter` field-for-field, in
// declared order. Can't carry `[GenerateTypeScript]` itself because `From`/`To` are
// `DateTimeOffset?` - see `$lib/memorypack/date-time-offset.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readNullableDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { SpanAttributeFilter } from '$lib/memorypack/SpanAttributeFilter';

export class SpanFilter {
	from: Date | null;
	to: Date | null;
	services: (string | null)[] | null;
	kinds: number[] | null;
	statusCodes: (string | null)[] | null;
	traceId: string | null;
	rootSpansOnly: boolean;
	minDurationNano: bigint | null;
	maxDurationNano: bigint | null;
	attributes: (SpanAttributeFilter | null)[] | null;

	constructor() {
		this.from = null;
		this.to = null;
		this.services = null;
		this.kinds = null;
		this.statusCodes = null;
		this.traceId = null;
		this.rootSpansOnly = false;
		this.minDurationNano = null;
		this.maxDurationNano = null;
		this.attributes = null;
	}

	static serialize(value: SpanFilter | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SpanFilter | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(10);
		writeNullableDateTimeOffset(writer, value.from);
		writeNullableDateTimeOffset(writer, value.to);
		writer.writeArray(value.services, (writer, x) => writer.writeString(x));
		writer.writeArray(value.kinds, (writer, x) => writer.writeUint8(x));
		writer.writeArray(value.statusCodes, (writer, x) => writer.writeString(x));
		writer.writeString(value.traceId);
		writer.writeBoolean(value.rootSpansOnly);
		writer.writeNullableUint64(value.minDurationNano);
		writer.writeNullableUint64(value.maxDurationNano);
		writer.writeArray(value.attributes, (writer, x) => SpanAttributeFilter.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): SpanFilter | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SpanFilter | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SpanFilter();
		if (count == 10) {
			value.from = readNullableDateTimeOffset(reader);
			value.to = readNullableDateTimeOffset(reader);
			value.services = reader.readArray((reader) => reader.readString());
			value.kinds = reader.readArray((reader) => reader.readUint8());
			value.statusCodes = reader.readArray((reader) => reader.readString());
			value.traceId = reader.readString();
			value.rootSpansOnly = reader.readBoolean();
			value.minDurationNano = reader.readNullableUint64();
			value.maxDurationNano = reader.readNullableUint64();
			value.attributes = reader.readArray((reader) => SpanAttributeFilter.deserializeCore(reader));
		} else if (count > 10) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.from = readNullableDateTimeOffset(reader);
			if (count == 1) return value;
			value.to = readNullableDateTimeOffset(reader);
			if (count == 2) return value;
			value.services = reader.readArray((reader) => reader.readString());
			if (count == 3) return value;
			value.kinds = reader.readArray((reader) => reader.readUint8());
			if (count == 4) return value;
			value.statusCodes = reader.readArray((reader) => reader.readString());
			if (count == 5) return value;
			value.traceId = reader.readString();
			if (count == 6) return value;
			value.rootSpansOnly = reader.readBoolean();
			if (count == 7) return value;
			value.minDurationNano = reader.readNullableUint64();
			if (count == 8) return value;
			value.maxDurationNano = reader.readNullableUint64();
			if (count == 9) return value;
			value.attributes = reader.readArray((reader) => SpanAttributeFilter.deserializeCore(reader));
			if (count == 10) return value;
		}
		return value;
	}
}
