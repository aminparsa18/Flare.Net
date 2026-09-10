// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ErrorModels.cs`'s `ExceptionGroup` field-for-field,
// in declared order. Can't carry `[GenerateTypeScript]` itself because `FirstSeen`/`LastSeen`
// are `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class ExceptionGroup {
	exceptionType: string | null;
	exceptionMessage: string | null;
	occurrenceCount: bigint;
	firstSeen: Date;
	lastSeen: Date;
	affectedServices: (string | null)[] | null;

	constructor() {
		this.exceptionType = null;
		this.exceptionMessage = null;
		this.occurrenceCount = 0n;
		this.firstSeen = new Date(0);
		this.lastSeen = new Date(0);
		this.affectedServices = null;
	}

	static serialize(value: ExceptionGroup | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExceptionGroup | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeString(value.exceptionType);
		writer.writeString(value.exceptionMessage);
		writer.writeUint64(value.occurrenceCount);
		writeDateTimeOffset(writer, value.firstSeen);
		writeDateTimeOffset(writer, value.lastSeen);
		writer.writeArray(value.affectedServices, (writer, x) => writer.writeString(x));
	}

	static serializeArray(value: (ExceptionGroup | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (ExceptionGroup | null)[] | null): void {
		writer.writeArray(value, (writer, x) => ExceptionGroup.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ExceptionGroup | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExceptionGroup | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExceptionGroup();
		if (count == 6) {
			value.exceptionType = reader.readString();
			value.exceptionMessage = reader.readString();
			value.occurrenceCount = reader.readUint64();
			value.firstSeen = readDateTimeOffset(reader);
			value.lastSeen = readDateTimeOffset(reader);
			value.affectedServices = reader.readArray((reader) => reader.readString());
		} else if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.exceptionType = reader.readString();
			if (count == 1) return value;
			value.exceptionMessage = reader.readString();
			if (count == 2) return value;
			value.occurrenceCount = reader.readUint64();
			if (count == 3) return value;
			value.firstSeen = readDateTimeOffset(reader);
			if (count == 4) return value;
			value.lastSeen = readDateTimeOffset(reader);
			if (count == 5) return value;
			value.affectedServices = reader.readArray((reader) => reader.readString());
			if (count == 6) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (ExceptionGroup | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (ExceptionGroup | null)[] | null {
		return reader.readArray((reader) => ExceptionGroup.deserializeCore(reader));
	}
}
