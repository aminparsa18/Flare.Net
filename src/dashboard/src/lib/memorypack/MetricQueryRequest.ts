// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricQueryRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `MetricFilter` (blocked - see `$lib/memorypack/MetricFilter.ts`'s header comment).
// `havingOperator`/`havingValue` were appended after every pre-existing field (same
// versioning convention AlertRuleRequest.ts documents) when the HAVING post-aggregation
// filter was added - see MetricModels.cs' MetricHavingOperator/MetricQueryRequest remarks.
// `postProcessFunctions` was appended the same way for ADR-0038's post-processing chain -
// `MetricPostProcessFunction` itself has no DateTimeOffset/nesting problem, so it's a real
// generated class (`$lib/generated/memorypack/MetricPostProcessFunction.js`), reused here
// directly the same way MetricFilter.ts reuses the generated `MetricAttributeFilter`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricFilter } from '$lib/memorypack/MetricFilter';
import { MetricPostProcessFunction } from '$lib/generated/memorypack/MetricPostProcessFunction.js';

export class MetricQueryRequest {
	metricName: string | null;
	type: number;
	filter: MetricFilter | null;
	bucketWidthSeconds: number;
	groupByAttributeKey: string | null;
	topN: number | null;
	havingOperator: number | null;
	havingValue: number | null;
	postProcessFunctions: (MetricPostProcessFunction | null)[] | null;

	constructor() {
		this.metricName = null;
		this.type = 0;
		this.filter = null;
		this.bucketWidthSeconds = 0;
		this.groupByAttributeKey = null;
		this.topN = null;
		this.havingOperator = null;
		this.havingValue = null;
		this.postProcessFunctions = null;
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

		writer.writeObjectHeader(9);
		writer.writeString(value.metricName);
		writer.writeInt32(value.type);
		MetricFilter.serializeCore(writer, value.filter);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeString(value.groupByAttributeKey);
		writer.writeNullableInt32(value.topN);
		writer.writeNullableInt32(value.havingOperator);
		writer.writeNullableFloat64(value.havingValue);
		writer.writeArray(value.postProcessFunctions, (writer, x) => MetricPostProcessFunction.serializeCore(writer, x));
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
		if (count == 9) {
			value.metricName = reader.readString();
			value.type = reader.readInt32();
			value.filter = MetricFilter.deserializeCore(reader);
			value.bucketWidthSeconds = reader.readInt32();
			value.groupByAttributeKey = reader.readString();
			value.topN = reader.readNullableInt32();
			value.havingOperator = reader.readNullableInt32();
			value.havingValue = reader.readNullableFloat64();
			value.postProcessFunctions = reader.readArray((reader) => MetricPostProcessFunction.deserializeCore(reader));
		} else if (count > 9) {
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
			value.topN = reader.readNullableInt32();
			if (count == 6) return value;
			value.havingOperator = reader.readNullableInt32();
			if (count == 7) return value;
			value.havingValue = reader.readNullableFloat64();
			if (count == 8) return value;
			value.postProcessFunctions = reader.readArray((reader) => MetricPostProcessFunction.deserializeCore(reader));
			if (count == 9) return value;
		}
		return value;
	}
}
