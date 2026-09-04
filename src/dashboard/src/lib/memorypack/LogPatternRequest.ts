// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogPatternModels.cs`'s `LogPatternRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `LogFilter` (blocked - see `$lib/memorypack/LogFilter.ts`'s header comment).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogFilter } from '$lib/memorypack/LogFilter';

export class LogPatternRequest {
	filter: LogFilter | null;
	topN: number | null;

	constructor() {
		this.filter = null;
		this.topN = null;
	}

	static serialize(value: LogPatternRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogPatternRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		LogFilter.serializeCore(writer, value.filter);
		writer.writeNullableInt32(value.topN);
	}

	static deserialize(buffer: ArrayBuffer): LogPatternRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogPatternRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogPatternRequest();
		if (count == 2) {
			value.filter = LogFilter.deserializeCore(reader);
			value.topN = reader.readNullableInt32();
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = LogFilter.deserializeCore(reader);
			if (count == 1) return value;
			value.topN = reader.readNullableInt32();
			if (count == 2) return value;
		}
		return value;
	}
}
