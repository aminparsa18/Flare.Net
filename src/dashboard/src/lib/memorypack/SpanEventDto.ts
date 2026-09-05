// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanDto.cs`'s `SpanEventDto` field-for-field, in
// declared order. Can't carry `[GenerateTypeScript]` itself because `Timestamp` is a
// `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { readStringRecord, writeStringRecord, type StringRecord } from '$lib/memorypack/string-record';

export class SpanEventDto {
	timestamp: Date;
	name: string | null;
	attributes: StringRecord;

	constructor() {
		this.timestamp = new Date(0);
		this.name = null;
		this.attributes = null;
	}

	static serialize(value: SpanEventDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SpanEventDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writeDateTimeOffset(writer, value.timestamp);
		writer.writeString(value.name);
		writeStringRecord(writer, value.attributes);
	}

	static serializeArray(value: (SpanEventDto | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (SpanEventDto | null)[] | null): void {
		writer.writeArray(value, (writer, x) => SpanEventDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): SpanEventDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SpanEventDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SpanEventDto();
		if (count == 3) {
			value.timestamp = readDateTimeOffset(reader);
			value.name = reader.readString();
			value.attributes = readStringRecord(reader);
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.timestamp = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.name = reader.readString();
			if (count == 2) return value;
			value.attributes = readStringRecord(reader);
			if (count == 3) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (SpanEventDto | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (SpanEventDto | null)[] | null {
		return reader.readArray((reader) => SpanEventDto.deserializeCore(reader));
	}
}
