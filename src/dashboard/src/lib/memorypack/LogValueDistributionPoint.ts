// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogValueDistributionRequest.cs`'s
// `LogValueDistributionPoint` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself because `Timestamp` is a `DateTimeOffset` - see
// `$lib/memorypack/date-time-offset.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class LogValueDistributionPoint {
	timestamp: Date;
	value: number;

	constructor() {
		this.timestamp = new Date(0);
		this.value = 0;
	}

	static serialize(value: LogValueDistributionPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogValueDistributionPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writeDateTimeOffset(writer, value.timestamp);
		writer.writeFloat64(value.value);
	}

	static serializeArray(value: (LogValueDistributionPoint | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (LogValueDistributionPoint | null)[] | null): void {
		writer.writeArray(value, (writer, x) => LogValueDistributionPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogValueDistributionPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogValueDistributionPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogValueDistributionPoint();
		if (count == 2) {
			value.timestamp = readDateTimeOffset(reader);
			value.value = reader.readFloat64();
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.timestamp = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.value = reader.readFloat64();
			if (count == 2) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (LogValueDistributionPoint | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (LogValueDistributionPoint | null)[] | null {
		return reader.readArray((reader) => LogValueDistributionPoint.deserializeCore(reader));
	}
}
