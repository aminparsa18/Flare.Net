// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricAttributeKeysRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `MetricFilter` (blocked - see `$lib/memorypack/MetricFilter.ts`'s header comment).
// `type` is a raw MemoryPack numeric ordinal (converted to string at `metrics-api.ts`'s
// module boundary via `$lib/memorypack/enums.ts`'s `metricPointTypeFromString`).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricFilter } from '$lib/memorypack/MetricFilter';

export class MetricAttributeKeysRequest {
	metricName: string | null;
	type: number;
	filter: MetricFilter | null;

	constructor() {
		this.metricName = null;
		this.type = 0;
		this.filter = null;
	}

	static serialize(value: MetricAttributeKeysRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricAttributeKeysRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeString(value.metricName);
		writer.writeInt32(value.type);
		MetricFilter.serializeCore(writer, value.filter);
	}

	static deserialize(buffer: ArrayBuffer): MetricAttributeKeysRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricAttributeKeysRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricAttributeKeysRequest();
		if (count == 3) {
			value.metricName = reader.readString();
			value.type = reader.readInt32();
			value.filter = MetricFilter.deserializeCore(reader);
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.metricName = reader.readString();
			if (count == 1) return value;
			value.type = reader.readInt32();
			if (count == 2) return value;
			value.filter = MetricFilter.deserializeCore(reader);
			if (count == 3) return value;
		}
		return value;
	}
}
