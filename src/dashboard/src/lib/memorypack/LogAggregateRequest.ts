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
// `groupByAttributeBag`/`groupByAttributeKey` were appended after that, same convention -
// the bag is a raw `AttributeBag` ordinal (`$lib/memorypack/enums.ts`'s
// `attributeBagFromString`).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogFilter } from '$lib/memorypack/LogFilter';
import { LogPostProcessFunction } from '$lib/generated/memorypack/LogPostProcessFunction.js';

export class LogAggregateRequest {
	filter: LogFilter | null;
	bucketWidthSeconds: number;
	groupBy: number;
	postProcessFunctions: (LogPostProcessFunction | null)[] | null;
	groupByAttributeBag: number;
	groupByAttributeKey: string | null;

	constructor() {
		this.filter = null;
		this.bucketWidthSeconds = 0;
		this.groupBy = 0;
		this.postProcessFunctions = null;
		this.groupByAttributeBag = 0;
		this.groupByAttributeKey = null;
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

		writer.writeObjectHeader(6);
		LogFilter.serializeCore(writer, value.filter);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeInt32(value.groupBy);
		writer.writeArray(value.postProcessFunctions, (writer, x) => LogPostProcessFunction.serializeCore(writer, x));
		writer.writeInt32(value.groupByAttributeBag);
		writer.writeString(value.groupByAttributeKey);
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
		if (count == 6) {
			value.filter = LogFilter.deserializeCore(reader);
			value.bucketWidthSeconds = reader.readInt32();
			value.groupBy = reader.readInt32();
			value.postProcessFunctions = reader.readArray((reader) => LogPostProcessFunction.deserializeCore(reader));
			value.groupByAttributeBag = reader.readInt32();
			value.groupByAttributeKey = reader.readString();
		} else if (count > 6) {
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
			value.groupByAttributeBag = reader.readInt32();
			if (count == 5) return value;
			value.groupByAttributeKey = reader.readString();
			if (count == 6) return value;
		}
		return value;
	}
}
