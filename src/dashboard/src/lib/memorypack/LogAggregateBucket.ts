// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogAggregateRequest.cs`'s `LogAggregateBucket`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `BucketStart` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment. Also reused by `LogQlQueryResponse` (the SQL-query-row feature's `Series` kind -
// same bucket shape `/api/logs/aggregate` returns).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class LogAggregateBucket {
	bucketStart: Date;
	groupKey: string | null;
	count: number;

	constructor() {
		this.bucketStart = new Date(0);
		this.groupKey = null;
		this.count = 0;
	}

	static serialize(value: LogAggregateBucket | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogAggregateBucket | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writeDateTimeOffset(writer, value.bucketStart);
		writer.writeString(value.groupKey);
		writer.writeFloat64(value.count);
	}

	static serializeArray(value: (LogAggregateBucket | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (LogAggregateBucket | null)[] | null): void {
		writer.writeArray(value, (writer, x) => LogAggregateBucket.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogAggregateBucket | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogAggregateBucket | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogAggregateBucket();
		if (count == 3) {
			value.bucketStart = readDateTimeOffset(reader);
			value.groupKey = reader.readString();
			value.count = reader.readFloat64();
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bucketStart = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.groupKey = reader.readString();
			if (count == 2) return value;
			value.count = reader.readFloat64();
			if (count == 3) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (LogAggregateBucket | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (LogAggregateBucket | null)[] | null {
		return reader.readArray((reader) => LogAggregateBucket.deserializeCore(reader));
	}
}
