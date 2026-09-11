// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogFilter.cs`'s `AttributeFilter` field-for-field,
// in declared order. Individually has no DateTimeOffset/JsonElement/IReadOnlyList member, so
// it *could* carry `[GenerateTypeScript]` on its own - but its only consumer, `LogFilter`,
// can't (its `From`/`To` are `DateTimeOffset?`), and the generator's nested-object import is
// a hardcoded same-directory `./{Type}.js` reference, so a generated `AttributeFilter`
// wouldn't help a hand-written `LogFilter` that has to import it from
// `$lib/memorypack/` instead. Hand-written for consistency with `LogFilter`, its only user.
// `bag`/`operator` are raw MemoryPack numeric ordinals (converted to string at
// `$lib/memorypack/LogFilter.ts`'s mapping boundary, via `$lib/memorypack/enums.ts`'s
// `attributeBagToString`/`FromString` and `attributeFilterOperatorToString`/`FromString`).
// `operator` is member 4, appended after the original 3 (`Flare.Api`'s own C# comment on
// `AttributeFilter.Operator` explains why it had to land last, not inserted) - the
// count-gated fallback branch below is what lets this file's own already-deployed builds
// (still writing/reading only 3 members) stay wire-compatible with a `Flare.Api` that now
// has 4, and vice versa. `values` is member 5, appended after `operator` for the identical
// reason - the `In`/`NotIn` operators' multi-value operand (`AttributeFilter.Values` in the
// C# source).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class AttributeFilter {
	bag: number;
	key: string | null;
	value: string | null;
	operator: number;
	values: (string | null)[] | null;

	constructor() {
		this.bag = 0;
		this.key = null;
		this.value = null;
		this.operator = 0;
		this.values = null;
	}

	static serialize(value: AttributeFilter | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: AttributeFilter | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writer.writeInt32(value.bag);
		writer.writeString(value.key);
		writer.writeString(value.value);
		writer.writeInt32(value.operator);
		writer.writeArray(value.values, (writer, x) => writer.writeString(x));
	}

	static serializeArray(value: (AttributeFilter | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (AttributeFilter | null)[] | null): void {
		writer.writeArray(value, (writer, x) => AttributeFilter.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): AttributeFilter | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): AttributeFilter | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new AttributeFilter();
		if (count == 5) {
			value.bag = reader.readInt32();
			value.key = reader.readString();
			value.value = reader.readString();
			value.operator = reader.readInt32();
			value.values = reader.readArray((reader) => reader.readString());
		} else if (count > 5) {
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
			value.values = reader.readArray((reader) => reader.readString());
			if (count == 5) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (AttributeFilter | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (AttributeFilter | null)[] | null {
		return reader.readArray((reader) => AttributeFilter.deserializeCore(reader));
	}
}
