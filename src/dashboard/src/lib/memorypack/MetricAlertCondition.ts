// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `MetricAlertCondition`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `MetricFilter` (blocked - see `$lib/memorypack/MetricFilter.ts`'s header comment).
// `type`/`aggregation` are plain numbers, not imported generated enums - see
// `$lib/memorypack/enums.ts`'s `AlertConditionKindName`/`MetricAlertAggregationName` remarks
// for why.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricFilter } from '$lib/memorypack/MetricFilter';

export class MetricAlertCondition {
	metricName: string | null;
	type: number;
	filter: MetricFilter | null;
	aggregation: number;

	constructor() {
		this.metricName = null;
		this.type = 0;
		this.filter = null;
		this.aggregation = 0;
	}

	static serialize(value: MetricAlertCondition | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricAlertCondition | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.metricName);
		writer.writeInt32(value.type);
		MetricFilter.serializeCore(writer, value.filter);
		writer.writeInt32(value.aggregation);
	}

	static deserialize(buffer: ArrayBuffer): MetricAlertCondition | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricAlertCondition | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricAlertCondition();
		if (count == 4) {
			value.metricName = reader.readString();
			value.type = reader.readInt32();
			value.filter = MetricFilter.deserializeCore(reader);
			value.aggregation = reader.readInt32();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.metricName = reader.readString();
			if (count == 1) return value;
			value.type = reader.readInt32();
			if (count == 2) return value;
			value.filter = MetricFilter.deserializeCore(reader);
			if (count == 3) return value;
			value.aggregation = reader.readInt32();
			if (count == 4) return value;
		}
		return value;
	}
}
