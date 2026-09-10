// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogAttributeValuesRequest.cs`'s
// `LogAttributeValuesRequest` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself because it nests `LogFilter` (blocked - see
// `$lib/memorypack/LogFilter.ts`'s header comment). `bag` is a raw MemoryPack numeric
// ordinal, same "convert at the api.ts mapping boundary" convention `AttributeFilter.ts`
// documents for its own `bag`/`operator` members.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogFilter } from '$lib/memorypack/LogFilter';

export class LogAttributeValuesRequest {
	filter: LogFilter | null;
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

	static serialize(value: LogAttributeValuesRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogAttributeValuesRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		LogFilter.serializeCore(writer, value.filter);
		writer.writeInt32(value.bag);
		writer.writeString(value.key);
		writer.writeString(value.prefix);
		writer.writeInt32(value.limit);
	}

	static deserialize(buffer: ArrayBuffer): LogAttributeValuesRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogAttributeValuesRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogAttributeValuesRequest();
		if (count == 5) {
			value.filter = LogFilter.deserializeCore(reader);
			value.bag = reader.readInt32();
			value.key = reader.readString();
			value.prefix = reader.readString();
			value.limit = reader.readInt32();
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = LogFilter.deserializeCore(reader);
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
