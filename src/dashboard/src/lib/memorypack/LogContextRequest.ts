// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogContextRequest.cs`'s `LogContextRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `Timestamp` is `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class LogContextRequest {
	eventId: string;
	timestamp: Date;
	before: number | null;
	after: number | null;

	constructor() {
		this.eventId = '00000000-0000-0000-0000-000000000000';
		this.timestamp = new Date(0);
		this.before = null;
		this.after = null;
	}

	static serialize(value: LogContextRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogContextRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeGuid(value.eventId);
		writeDateTimeOffset(writer, value.timestamp);
		writer.writeNullableInt32(value.before);
		writer.writeNullableInt32(value.after);
	}

	static deserialize(buffer: ArrayBuffer): LogContextRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogContextRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogContextRequest();
		if (count == 4) {
			value.eventId = reader.readGuid();
			value.timestamp = readDateTimeOffset(reader);
			value.before = reader.readNullableInt32();
			value.after = reader.readNullableInt32();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.eventId = reader.readGuid();
			if (count == 1) return value;
			value.timestamp = readDateTimeOffset(reader);
			if (count == 2) return value;
			value.before = reader.readNullableInt32();
			if (count == 3) return value;
			value.after = reader.readNullableInt32();
			if (count == 4) return value;
		}
		return value;
	}
}
