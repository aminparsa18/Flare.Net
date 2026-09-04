// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogSearchRequest.cs`'s `LogSearchRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `LogFilter` (blocked - see `$lib/memorypack/LogFilter.ts`'s header comment).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogFilter } from '$lib/memorypack/LogFilter';

export class LogSearchRequest {
	filter: LogFilter | null;
	cursor: string | null;
	pageSize: number | null;
	includeSpanDuration: boolean;

	constructor() {
		this.filter = null;
		this.cursor = null;
		this.pageSize = null;
		this.includeSpanDuration = false;
	}

	static serialize(value: LogSearchRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogSearchRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		LogFilter.serializeCore(writer, value.filter);
		writer.writeString(value.cursor);
		writer.writeNullableInt32(value.pageSize);
		writer.writeBoolean(value.includeSpanDuration);
	}

	static deserialize(buffer: ArrayBuffer): LogSearchRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogSearchRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogSearchRequest();
		if (count == 4) {
			value.filter = LogFilter.deserializeCore(reader);
			value.cursor = reader.readString();
			value.pageSize = reader.readNullableInt32();
			value.includeSpanDuration = reader.readBoolean();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = LogFilter.deserializeCore(reader);
			if (count == 1) return value;
			value.cursor = reader.readString();
			if (count == 2) return value;
			value.pageSize = reader.readNullableInt32();
			if (count == 3) return value;
			value.includeSpanDuration = reader.readBoolean();
			if (count == 4) return value;
		}
		return value;
	}
}
