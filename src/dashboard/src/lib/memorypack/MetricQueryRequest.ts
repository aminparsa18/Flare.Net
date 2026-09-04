// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricQueryRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `MetricFilter` (blocked - see `$lib/memorypack/MetricFilter.ts`'s header comment).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricFilter } from '$lib/memorypack/MetricFilter';

export class MetricQueryRequest {
	metricName: string | null;
	type: number;
	filter: MetricFilter | null;
	bucketWidthSeconds: number;
	groupByAttributeKey: string | null;

	constructor() {
		this.metricName = null;
		this.type = 0;
		this.filter = null;
		this.bucketWidthSeconds = 0;
		this.groupByAttributeKey = null;
	}

	static serialize(value: MetricQueryRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricQueryRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writer.writeString(value.metricName);
		writer.writeInt32(value.type);
		MetricFilter.serializeCore(writer, value.filter);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeString(value.groupByAttributeKey);
	}

	static deserialize(buffer: ArrayBuffer): MetricQueryRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricQueryRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricQueryRequest();
		if (count == 5) {
			value.metricName = reader.readString();
			value.type = reader.readInt32();
			value.filter = MetricFilter.deserializeCore(reader);
			value.bucketWidthSeconds = reader.readInt32();
			value.groupByAttributeKey = reader.readString();
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.metricName = reader.readString();
			if (count == 1) return value;
			value.type = reader.readInt32();
			if (count == 2) return value;
			value.filter = MetricFilter.deserializeCore(reader);
			if (count == 3) return value;
			value.bucketWidthSeconds = reader.readInt32();
			if (count == 4) return value;
			value.groupByAttributeKey = reader.readString();
			if (count == 5) return value;
		}
		return value;
	}
}
