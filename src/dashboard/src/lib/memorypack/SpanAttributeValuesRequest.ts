// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanAttributeValuesRequest.cs`'s
// `SpanAttributeValuesRequest` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself because it nests `SpanFilter` (blocked - see
// `$lib/memorypack/SpanFilter.ts`'s header comment). `bag` is a raw MemoryPack numeric
// ordinal, same "convert at the api.ts mapping boundary" convention
// `$lib/memorypack/SpanAttributeFilter.ts` documents for its own `bag`/`operator` members.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { SpanFilter } from '$lib/memorypack/SpanFilter';

export class SpanAttributeValuesRequest {
	filter: SpanFilter | null;
	bag: number;
	key: string | null;
	prefix: string | null;
	limit: number;

	constructor() {
		this.filter = null;
		this.bag = 0;
		this.key = null;
		this.prefix = null;
		this.limit = 0;
	}

	static serialize(value: SpanAttributeValuesRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SpanAttributeValuesRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		SpanFilter.serializeCore(writer, value.filter);
		writer.writeInt32(value.bag);
		writer.writeString(value.key);
		writer.writeString(value.prefix);
		writer.writeInt32(value.limit);
	}

	static deserialize(buffer: ArrayBuffer): SpanAttributeValuesRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SpanAttributeValuesRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SpanAttributeValuesRequest();
		if (count == 5) {
			value.filter = SpanFilter.deserializeCore(reader);
			value.bag = reader.readInt32();
			value.key = reader.readString();
			value.prefix = reader.readString();
			value.limit = reader.readInt32();
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = SpanFilter.deserializeCore(reader);
			if (count == 1) return value;
			value.bag = reader.readInt32();
			if (count == 2) return value;
			value.key = reader.readString();
			if (count == 3) return value;
			value.prefix = reader.readString();
			if (count == 4) return value;
			value.limit = reader.readInt32();
			if (count == 5) return value;
		}
		return value;
	}
}
