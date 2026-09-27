// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/TraceStructureModels.cs`'s `TraceSpanCondition`,
// in declared order. Can't carry `[GenerateTypeScript]`: `Attributes` is an
// `IReadOnlyList<T>` (see ADR-0016).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { SpanAttributeFilter } from '$lib/memorypack/SpanAttributeFilter';

const MEMBER_COUNT = 6;

export class TraceSpanCondition {
	name: string | null = null;
	serviceName: string | null = null;
	spanName: string | null = null;
	statusCode: string | null = null;
	minDurationNano: bigint | null = null;
	attributes: (SpanAttributeFilter | null)[] | null = null;

	static serializeCore(writer: MemoryPackWriter, value: TraceSpanCondition | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(MEMBER_COUNT);
		writer.writeString(value.name);
		writer.writeString(value.serviceName);
		writer.writeString(value.spanName);
		writer.writeString(value.statusCode);
		writer.writeNullableUint64(value.minDurationNano);
		writer.writeArray(value.attributes, (writer, x) => SpanAttributeFilter.serializeCore(writer, x));
	}

	static deserializeCore(reader: MemoryPackReader): TraceSpanCondition | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}
		if (count > MEMBER_COUNT) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		}

		const value = new TraceSpanCondition();
		if (count == 0) return value;
		value.name = reader.readString();
		if (count == 1) return value;
		value.serviceName = reader.readString();
		if (count == 2) return value;
		value.spanName = reader.readString();
		if (count == 3) return value;
		value.statusCode = reader.readString();
		if (count == 4) return value;
		value.minDurationNano = reader.readNullableUint64();
		if (count == 5) return value;
		value.attributes = reader.readArray((reader) => SpanAttributeFilter.deserializeCore(reader));
		return value;
	}
}
