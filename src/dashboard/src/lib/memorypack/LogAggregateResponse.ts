// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogAggregateRequest.cs`'s `LogAggregateResponse`.
// Can't carry `[GenerateTypeScript]` itself because its one member's type,
// `LogAggregateBucket`, has a `DateTimeOffset` member - see `LogAggregateBucket.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogAggregateBucket } from '$lib/memorypack/LogAggregateBucket';

export class LogAggregateResponse {
	buckets: (LogAggregateBucket | null)[] | null;

	constructor() {
		this.buckets = null;
	}

	static serialize(value: LogAggregateResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogAggregateResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.buckets, (writer, x) => LogAggregateBucket.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogAggregateResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogAggregateResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogAggregateResponse();
		if (count == 1) {
			value.buckets = reader.readArray((reader) => LogAggregateBucket.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.buckets = reader.readArray((reader) => LogAggregateBucket.deserializeCore(reader));
		}
		return value;
	}
}
