// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricFilter` field-for-field,
// in declared order. Can't carry `[GenerateTypeScript]` itself because `From`/`To` are
// `DateTimeOffset?` - see `$lib/memorypack/date-time-offset.ts`'s header comment.
// `MetricAttributeFilter` has no such problem, so it's a real generated class, reused here
// directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricAttributeFilter } from '$lib/generated/memorypack/MetricAttributeFilter.js';
import { readNullableDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class MetricFilter {
	from: Date | null;
	to: Date | null;
	services: (string | null)[] | null;
	attributes: (MetricAttributeFilter | null)[] | null;

	constructor() {
		this.from = null;
		this.to = null;
		this.services = null;
		this.attributes = null;
	}

	static serialize(value: MetricFilter | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricFilter | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writeNullableDateTimeOffset(writer, value.from);
		writeNullableDateTimeOffset(writer, value.to);
		writer.writeArray(value.services, (writer, x) => writer.writeString(x));
		writer.writeArray(value.attributes, (writer, x) => MetricAttributeFilter.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): MetricFilter | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricFilter | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricFilter();
		if (count == 4) {
			value.from = readNullableDateTimeOffset(reader);
			value.to = readNullableDateTimeOffset(reader);
			value.services = reader.readArray((reader) => reader.readString());
			value.attributes = reader.readArray((reader) => MetricAttributeFilter.deserializeCore(reader));
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.from = readNullableDateTimeOffset(reader);
			if (count == 1) return value;
			value.to = readNullableDateTimeOffset(reader);
			if (count == 2) return value;
			value.services = reader.readArray((reader) => reader.readString());
			if (count == 3) return value;
			value.attributes = reader.readArray((reader) => MetricAttributeFilter.deserializeCore(reader));
			if (count == 4) return value;
		}
		return value;
	}
}
