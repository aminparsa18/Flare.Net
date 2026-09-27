// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ErrorModels.cs`'s `ExceptionFilter` field-for-field,
// in declared order. Can't carry `[GenerateTypeScript]` itself because `From`/`To` are
// `DateTimeOffset?` - see `$lib/memorypack/date-time-offset.ts`'s header comment.
// `resourceAttributes`' element type is generated - same split as `ServiceOverviewRequest.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ResourceAttributeFilter } from '$lib/generated/memorypack/ResourceAttributeFilter.js';
import { readNullableDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class ExceptionFilter {
	from: Date | null;
	to: Date | null;
	services: (string | null)[] | null;
	resourceAttributes: (ResourceAttributeFilter | null)[] | null;

	constructor() {
		this.from = null;
		this.to = null;
		this.services = null;
		this.resourceAttributes = null;
	}

	static serialize(value: ExceptionFilter | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExceptionFilter | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writeNullableDateTimeOffset(writer, value.from);
		writeNullableDateTimeOffset(writer, value.to);
		writer.writeArray(value.services, (writer, x) => writer.writeString(x));
		writer.writeArray(value.resourceAttributes, (writer, x) => ResourceAttributeFilter.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ExceptionFilter | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExceptionFilter | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExceptionFilter();
		if (count == 4) {
			value.from = readNullableDateTimeOffset(reader);
			value.to = readNullableDateTimeOffset(reader);
			value.services = reader.readArray((reader) => reader.readString());
			value.resourceAttributes = reader.readArray((reader) => ResourceAttributeFilter.deserializeCore(reader));
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
			value.resourceAttributes = reader.readArray((reader) => ResourceAttributeFilter.deserializeCore(reader));
			if (count == 4) return value;
		}
		return value;
	}
}
