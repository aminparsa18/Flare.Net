// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanSearchRequest.cs`'s `SpanSearchRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `SpanFilter` (blocked - see `$lib/memorypack/SpanFilter.ts`'s header comment).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { SpanFilter } from '$lib/memorypack/SpanFilter';

export class SpanSearchRequest {
	filter: SpanFilter | null;
	cursor: string | null;
	pageSize: number | null;

	constructor() {
		this.filter = null;
		this.cursor = null;
		this.pageSize = null;
	}

	static serialize(value: SpanSearchRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SpanSearchRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		SpanFilter.serializeCore(writer, value.filter);
		writer.writeString(value.cursor);
		writer.writeNullableInt32(value.pageSize);
	}

	static deserialize(buffer: ArrayBuffer): SpanSearchRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SpanSearchRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SpanSearchRequest();
		if (count == 3) {
			value.filter = SpanFilter.deserializeCore(reader);
			value.cursor = reader.readString();
			value.pageSize = reader.readNullableInt32();
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = SpanFilter.deserializeCore(reader);
			if (count == 1) return value;
			value.cursor = reader.readString();
			if (count == 2) return value;
			value.pageSize = reader.readNullableInt32();
			if (count == 3) return value;
		}
		return value;
	}
}
