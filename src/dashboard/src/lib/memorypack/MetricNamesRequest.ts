// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricNamesRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `From`/`To` are `DateTimeOffset?` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readNullableDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class MetricNamesRequest {
	from: Date | null;
	to: Date | null;
	services: (string | null)[] | null;

	constructor() {
		this.from = null;
		this.to = null;
		this.services = null;
	}

	static serialize(value: MetricNamesRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricNamesRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writeNullableDateTimeOffset(writer, value.from);
		writeNullableDateTimeOffset(writer, value.to);
		writer.writeArray(value.services, (writer, x) => writer.writeString(x));
	}

	static deserialize(buffer: ArrayBuffer): MetricNamesRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricNamesRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricNamesRequest();
		if (count == 3) {
			value.from = readNullableDateTimeOffset(reader);
			value.to = readNullableDateTimeOffset(reader);
			value.services = reader.readArray((reader) => reader.readString());
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.from = readNullableDateTimeOffset(reader);
			if (count == 1) return value;
			value.to = readNullableDateTimeOffset(reader);
			if (count == 2) return value;
			value.services = reader.readArray((reader) => reader.readString());
			if (count == 3) return value;
		}
		return value;
	}
}
