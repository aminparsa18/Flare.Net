// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PipelineModels.cs`'s `PipelineServiceBreakdown`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because its
// `TopServices` member is an `IReadOnlyList<PipelineServiceEntry>` - MemoryPack's TypeScript
// generator only recognizes the *mutable* collection interfaces (`ICollection<T>`/
// `ISet<T>`/`IDictionary<K,V>`) via `TypeMeta.ParseCollectionKind`, which
// `IReadOnlyList<T>`/`IReadOnlyDictionary<K,V>` do not extend - a declared `IReadOnlyList<T>`
// member throws the same `MEMPACK031` a `DateTimeOffset` member does. Confirmed by actually
// attaching `[GenerateTypeScript]` here and hitting exactly that error before switching to
// hand-writing it. `PipelineServiceEntry` itself has no such member, so it's a real
// generated class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { PipelineServiceEntry } from '$lib/generated/memorypack/PipelineServiceEntry.js';

export class PipelineServiceBreakdown {
	signal: number;
	topServices: (PipelineServiceEntry | null)[] | null;
	otherServiceCount: bigint;
	otherRecords: bigint;
	otherBytes: bigint;

	constructor() {
		this.signal = 0;
		this.topServices = null;
		this.otherServiceCount = 0n;
		this.otherRecords = 0n;
		this.otherBytes = 0n;
	}

	static serialize(value: PipelineServiceBreakdown | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PipelineServiceBreakdown | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writer.writeInt32(value.signal);
		writer.writeArray(value.topServices, (writer, x) => PipelineServiceEntry.serializeCore(writer, x));
		writer.writeInt64(value.otherServiceCount);
		writer.writeInt64(value.otherRecords);
		writer.writeInt64(value.otherBytes);
	}

	static serializeArray(value: (PipelineServiceBreakdown | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (PipelineServiceBreakdown | null)[] | null): void {
		writer.writeArray(value, (writer, x) => PipelineServiceBreakdown.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): PipelineServiceBreakdown | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PipelineServiceBreakdown | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PipelineServiceBreakdown();
		if (count == 5) {
			value.signal = reader.readInt32();
			value.topServices = reader.readArray((reader) => PipelineServiceEntry.deserializeCore(reader));
			value.otherServiceCount = reader.readInt64();
			value.otherRecords = reader.readInt64();
			value.otherBytes = reader.readInt64();
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.signal = reader.readInt32();
			if (count == 1) return value;
			value.topServices = reader.readArray((reader) => PipelineServiceEntry.deserializeCore(reader));
			if (count == 2) return value;
			value.otherServiceCount = reader.readInt64();
			if (count == 3) return value;
			value.otherRecords = reader.readInt64();
			if (count == 4) return value;
			value.otherBytes = reader.readInt64();
			if (count == 5) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (PipelineServiceBreakdown | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (PipelineServiceBreakdown | null)[] | null {
		return reader.readArray((reader) => PipelineServiceBreakdown.deserializeCore(reader));
	}
}
