// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PodMetricsModels.cs`'s `PodMetricsPoint`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `BucketStart` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class PodMetricsPoint {
	bucketStart: Date;
	cpuCores: number | null;
	memoryWorkingSetBytes: number | null;
	cpuLimitPercent: number | null;
	memoryLimitPercent: number | null;

	constructor() {
		this.bucketStart = new Date(0);
		this.cpuCores = null;
		this.memoryWorkingSetBytes = null;
		this.cpuLimitPercent = null;
		this.memoryLimitPercent = null;
	}

	static serialize(value: PodMetricsPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PodMetricsPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writeDateTimeOffset(writer, value.bucketStart);
		writer.writeNullableFloat64(value.cpuCores);
		writer.writeNullableFloat64(value.memoryWorkingSetBytes);
		writer.writeNullableFloat64(value.cpuLimitPercent);
		writer.writeNullableFloat64(value.memoryLimitPercent);
	}

	static deserialize(buffer: ArrayBuffer): PodMetricsPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PodMetricsPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PodMetricsPoint();
		if (count == 5) {
			value.bucketStart = readDateTimeOffset(reader);
			value.cpuCores = reader.readNullableFloat64();
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			value.cpuLimitPercent = reader.readNullableFloat64();
			value.memoryLimitPercent = reader.readNullableFloat64();
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bucketStart = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.cpuCores = reader.readNullableFloat64();
			if (count == 2) return value;
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			if (count == 3) return value;
			value.cpuLimitPercent = reader.readNullableFloat64();
			if (count == 4) return value;
			value.memoryLimitPercent = reader.readNullableFloat64();
		}
		return value;
	}
}
