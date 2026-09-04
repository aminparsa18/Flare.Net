// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogQlQueryRequest.cs`'s `LogQlQueryRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `From`/`To` are `DateTimeOffset?` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readNullableDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class LogQlQueryRequest {
	query: string | null;
	from: Date | null;
	to: Date | null;

	constructor() {
		this.query = null;
		this.from = null;
		this.to = null;
	}

	static serialize(value: LogQlQueryRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogQlQueryRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeString(value.query);
		writeNullableDateTimeOffset(writer, value.from);
		writeNullableDateTimeOffset(writer, value.to);
	}

	static deserialize(buffer: ArrayBuffer): LogQlQueryRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogQlQueryRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogQlQueryRequest();
		if (count == 3) {
			value.query = reader.readString();
			value.from = readNullableDateTimeOffset(reader);
			value.to = readNullableDateTimeOffset(reader);
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.query = reader.readString();
			if (count == 1) return value;
			value.from = readNullableDateTimeOffset(reader);
			if (count == 2) return value;
			value.to = readNullableDateTimeOffset(reader);
			if (count == 3) return value;
		}
		return value;
	}
}
