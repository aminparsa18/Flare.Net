// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/TraceStructureModels.cs`'s
// `TraceStructureFilter`, in declared order. Can't carry `[GenerateTypeScript]`:
// `Conditions` is an `IReadOnlyList<T>` (see ADR-0016).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { TraceSpanCondition } from '$lib/memorypack/TraceSpanCondition';

export class TraceStructureFilter {
	conditions: (TraceSpanCondition | null)[] | null = null;
	expression: string | null = null;

	static serialize(value: TraceStructureFilter | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: TraceStructureFilter | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writer.writeArray(value.conditions, (writer, x) => TraceSpanCondition.serializeCore(writer, x));
		writer.writeString(value.expression);
	}

	static deserializeCore(reader: MemoryPackReader): TraceStructureFilter | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}
		if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		}

		const value = new TraceStructureFilter();
		if (count == 0) return value;
		value.conditions = reader.readArray((reader) => TraceSpanCondition.deserializeCore(reader));
		if (count == 1) return value;
		value.expression = reader.readString();
		return value;
	}
}
