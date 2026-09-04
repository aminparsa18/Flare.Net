// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanFilter.cs`'s `SpanAttributeFilter`
// field-for-field, in declared order. Individually clean (no DateTimeOffset/JsonElement/
// IReadOnlyList member), but hand-written anyway since its only consumer, `SpanFilter`, is
// blocked (`DateTimeOffset?` members) and the generator's nested-object import is a
// hardcoded same-directory reference - see `LogFilter.ts`'s header comment for the same
// reasoning applied to `AttributeFilter`. `bag` is a raw MemoryPack numeric ordinal
// (converted to string at `traces-api.ts`'s module boundary).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class SpanAttributeFilter {
	bag: number;
	key: string | null;
	value: string | null;

	constructor() {
		this.bag = 0;
		this.key = null;
		this.value = null;
	}

	static serialize(value: SpanAttributeFilter | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SpanAttributeFilter | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeInt32(value.bag);
		writer.writeString(value.key);
		writer.writeString(value.value);
	}

	static serializeArray(value: (SpanAttributeFilter | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (SpanAttributeFilter | null)[] | null): void {
		writer.writeArray(value, (writer, x) => SpanAttributeFilter.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): SpanAttributeFilter | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SpanAttributeFilter | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SpanAttributeFilter();
		if (count == 3) {
			value.bag = reader.readInt32();
			value.key = reader.readString();
			value.value = reader.readString();
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bag = reader.readInt32();
			if (count == 1) return value;
			value.key = reader.readString();
			if (count == 2) return value;
			value.value = reader.readString();
			if (count == 3) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (SpanAttributeFilter | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (SpanAttributeFilter | null)[] | null {
		return reader.readArray((reader) => SpanAttributeFilter.deserializeCore(reader));
	}
}
