// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogFilter.cs`'s `BodyJsonFilter` field-for-field,
// in declared order - same "no DateTimeOffset/JsonElement/IReadOnlyList member on its own,
// but its only consumer (`LogFilter`) can't carry `[GenerateTypeScript]` either" reasoning
// `AttributeFilter.ts`'s own header gives. `operator` is a raw MemoryPack numeric ordinal
// (converted to string at `$lib/memorypack/LogFilter.ts`'s mapping boundary, via
// `$lib/memorypack/enums.ts`'s `bodyJsonFilterOperatorToString`/`FromString`).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class BodyJsonFilter {
	path: string | null;
	value: string | null;
	operator: number;
	values: (string | null)[] | null;

	constructor() {
		this.path = null;
		this.value = null;
		this.operator = 0;
		this.values = null;
	}

	static serialize(value: BodyJsonFilter | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: BodyJsonFilter | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.path);
		writer.writeString(value.value);
		writer.writeInt32(value.operator);
		writer.writeArray(value.values, (writer, x) => writer.writeString(x));
	}

	static serializeArray(value: (BodyJsonFilter | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (BodyJsonFilter | null)[] | null): void {
		writer.writeArray(value, (writer, x) => BodyJsonFilter.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): BodyJsonFilter | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): BodyJsonFilter | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new BodyJsonFilter();
		if (count == 4) {
			value.path = reader.readString();
			value.value = reader.readString();
			value.operator = reader.readInt32();
			value.values = reader.readArray((reader) => reader.readString());
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.path = reader.readString();
			if (count == 1) return value;
			value.value = reader.readString();
			if (count == 2) return value;
			value.operator = reader.readInt32();
			if (count == 3) return value;
			value.values = reader.readArray((reader) => reader.readString());
			if (count == 4) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (BodyJsonFilter | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (BodyJsonFilter | null)[] | null {
		return reader.readArray((reader) => BodyJsonFilter.deserializeCore(reader));
	}
}
