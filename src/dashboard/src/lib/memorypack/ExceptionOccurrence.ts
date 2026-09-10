// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ErrorModels.cs`'s `ExceptionOccurrence`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `Timestamp` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class ExceptionOccurrence {
	traceId: string | null;
	spanId: string | null;
	serviceName: string | null;
	spanName: string | null;
	timestamp: Date;
	stacktrace: string | null;

	constructor() {
		this.traceId = null;
		this.spanId = null;
		this.serviceName = null;
		this.spanName = null;
		this.timestamp = new Date(0);
		this.stacktrace = null;
	}

	static serialize(value: ExceptionOccurrence | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExceptionOccurrence | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeString(value.traceId);
		writer.writeString(value.spanId);
		writer.writeString(value.serviceName);
		writer.writeString(value.spanName);
		writeDateTimeOffset(writer, value.timestamp);
		writer.writeString(value.stacktrace);
	}

	static serializeArray(value: (ExceptionOccurrence | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (ExceptionOccurrence | null)[] | null): void {
		writer.writeArray(value, (writer, x) => ExceptionOccurrence.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ExceptionOccurrence | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExceptionOccurrence | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExceptionOccurrence();
		if (count == 6) {
			value.traceId = reader.readString();
			value.spanId = reader.readString();
			value.serviceName = reader.readString();
			value.spanName = reader.readString();
			value.timestamp = readDateTimeOffset(reader);
			value.stacktrace = reader.readString();
		} else if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.traceId = reader.readString();
			if (count == 1) return value;
			value.spanId = reader.readString();
			if (count == 2) return value;
			value.serviceName = reader.readString();
			if (count == 3) return value;
			value.spanName = reader.readString();
			if (count == 4) return value;
			value.timestamp = readDateTimeOffset(reader);
			if (count == 5) return value;
			value.stacktrace = reader.readString();
			if (count == 6) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (ExceptionOccurrence | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (ExceptionOccurrence | null)[] | null {
		return reader.readArray((reader) => ExceptionOccurrence.deserializeCore(reader));
	}
}
