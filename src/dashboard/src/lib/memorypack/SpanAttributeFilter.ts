// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanFilter.cs`'s `SpanAttributeFilter`
// field-for-field, in declared order. Individually clean (no DateTimeOffset/JsonElement/
// IReadOnlyList member), but hand-written anyway since its only consumer, `SpanFilter`, is
// blocked (`DateTimeOffset?` members) and the generator's nested-object import is a
// hardcoded same-directory reference - see `LogFilter.ts`'s header comment for the same
// reasoning applied to `AttributeFilter`. `bag`/`operator` are raw MemoryPack numeric
// ordinals (converted to string at `traces-api.ts`'s module boundary, via
// `spanAttributeBagToString`/`FromString` and `spanAttributeFilterOperatorToString`/
// `FromString`). `operator` is member 4, appended after the original 3 (`Flare.Api`'s own
// C# comment on `SpanAttributeFilter.Operator` explains why it had to land last, not
// inserted) - the count-gated fallback branch below keeps this file's own already-deployed
// builds (still writing/reading only 3 members) wire-compatible with a `Flare.Api` that
// now has 4, and vice versa - same reasoning `AttributeFilter.ts` documents for its own
// identical change.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class SpanAttributeFilter {
	bag: number;
	key: string | null;
	value: string | null;
	operator: number;

	constructor() {
		this.bag = 0;
		this.key = null;
		this.value = null;
		this.operator = 0;
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

		writer.writeObjectHeader(4);
		writer.writeInt32(value.bag);
		writer.writeString(value.key);
		writer.writeString(value.value);
		writer.writeInt32(value.operator);
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
		if (count == 4) {
			value.bag = reader.readInt32();
			value.key = reader.readString();
			value.value = reader.readString();
			value.operator = reader.readInt32();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bag = reader.readInt32();
			if (count == 1) return value;
			value.key = reader.readString();
			if (count == 2) return value;
			value.value = reader.readString();
			if (count == 3) return value;
			value.operator = reader.readInt32();
			if (count == 4) return value;
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
