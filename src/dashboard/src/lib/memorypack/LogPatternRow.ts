// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogPatternModels.cs`'s `LogPatternRow`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `FirstSeen`/`LastSeen` are `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s
// header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class LogPatternRow {
	patternId: string | null;
	template: string | null;
	count: bigint;
	errorCount: bigint;
	firstSeen: Date;
	lastSeen: Date;

	constructor() {
		this.patternId = null;
		this.template = null;
		this.count = 0n;
		this.errorCount = 0n;
		this.firstSeen = new Date(0);
		this.lastSeen = new Date(0);
	}

	static serialize(value: LogPatternRow | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogPatternRow | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeString(value.patternId);
		writer.writeString(value.template);
		writer.writeInt64(value.count);
		writer.writeInt64(value.errorCount);
		writeDateTimeOffset(writer, value.firstSeen);
		writeDateTimeOffset(writer, value.lastSeen);
	}

	static serializeArray(value: (LogPatternRow | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (LogPatternRow | null)[] | null): void {
		writer.writeArray(value, (writer, x) => LogPatternRow.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogPatternRow | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogPatternRow | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogPatternRow();
		if (count == 6) {
			value.patternId = reader.readString();
			value.template = reader.readString();
			value.count = reader.readInt64();
			value.errorCount = reader.readInt64();
			value.firstSeen = readDateTimeOffset(reader);
			value.lastSeen = readDateTimeOffset(reader);
		} else if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.patternId = reader.readString();
			if (count == 1) return value;
			value.template = reader.readString();
			if (count == 2) return value;
			value.count = reader.readInt64();
			if (count == 3) return value;
			value.errorCount = reader.readInt64();
			if (count == 4) return value;
			value.firstSeen = readDateTimeOffset(reader);
			if (count == 5) return value;
			value.lastSeen = readDateTimeOffset(reader);
			if (count == 6) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (LogPatternRow | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (LogPatternRow | null)[] | null {
		return reader.readArray((reader) => LogPatternRow.deserializeCore(reader));
	}
}
