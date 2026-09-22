// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogAggregateRequest.cs`'s `LogAggregateRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `LogFilter` (blocked - see `$lib/memorypack/LogFilter.ts`'s header comment).
// `groupBy` is a raw MemoryPack numeric ordinal (converted to string at `api.ts`'s module
// boundary via `$lib/memorypack/enums.ts`'s `logAggregateGroupByFromString`).
// `postProcessFunctions` was appended after every pre-existing field for ADR-0041's
// post-processing chain, same versioning convention `MetricQueryRequest.ts`'s own
// `postProcessFunctions` append documents - `LogPostProcessFunction` itself has no
// DateTimeOffset/nesting problem, so it's a real generated class
// (`$lib/generated/memorypack/LogPostProcessFunction.js`), reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogFilter } from '$lib/memorypack/LogFilter';
import { LogPostProcessFunction } from '$lib/generated/memorypack/LogPostProcessFunction.js';

export class LogAggregateRequest {
	filter: LogFilter | null;
	bucketWidthSeconds: number;
	groupBy: number;
	postProcessFunctions: (LogPostProcessFunction | null)[] | null;

	constructor() {
		this.filter = null;
		this.bucketWidthSeconds = 0;
		this.groupBy = 0;
		this.postProcessFunctions = null;
	}

	static serialize(value: LogAggregateRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogAggregateRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		LogFilter.serializeCore(writer, value.filter);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeInt32(value.groupBy);
		writer.writeArray(value.postProcessFunctions, (writer, x) => LogPostProcessFunction.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogAggregateRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogAggregateRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogAggregateRequest();
		if (count == 4) {
			value.filter = LogFilter.deserializeCore(reader);
			value.bucketWidthSeconds = reader.readInt32();
			value.groupBy = reader.readInt32();
			value.postProcessFunctions = reader.readArray((reader) => LogPostProcessFunction.deserializeCore(reader));
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = LogFilter.deserializeCore(reader);
			if (count == 1) return value;
			value.bucketWidthSeconds = reader.readInt32();
			if (count == 2) return value;
			value.groupBy = reader.readInt32();
			if (count == 3) return value;
			value.postProcessFunctions = reader.readArray((reader) => LogPostProcessFunction.deserializeCore(reader));
			if (count == 4) return value;
		}
		return value;
	}
}
