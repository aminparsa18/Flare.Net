// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesInventoryModels.cs`'s `KubernetesNodeMetricsPoint`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself because it has a `DateTimeOffset` member - see `$lib/memorypack/date-time-offset.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class KubernetesNodeMetricsPoint {
	bucketStart: Date;
	cpuCores: number | null;
	cpuPercent: number | null;
	memoryWorkingSetBytes: number | null;
	memoryPercent: number | null;

	constructor() {
		this.bucketStart = new Date(0);
		this.cpuCores = null;
		this.cpuPercent = null;
		this.memoryWorkingSetBytes = null;
		this.memoryPercent = null;
	}

	static serialize(value: KubernetesNodeMetricsPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesNodeMetricsPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writeDateTimeOffset(writer, value.bucketStart);
		writer.writeNullableFloat64(value.cpuCores);
		writer.writeNullableFloat64(value.cpuPercent);
		writer.writeNullableFloat64(value.memoryWorkingSetBytes);
		writer.writeNullableFloat64(value.memoryPercent);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesNodeMetricsPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesNodeMetricsPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesNodeMetricsPoint();
		if (count == 5) {
			value.bucketStart = readDateTimeOffset(reader);
			value.cpuCores = reader.readNullableFloat64();
			value.cpuPercent = reader.readNullableFloat64();
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			value.memoryPercent = reader.readNullableFloat64();
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bucketStart = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.cpuCores = reader.readNullableFloat64();
			if (count == 2) return value;
			value.cpuPercent = reader.readNullableFloat64();
			if (count == 3) return value;
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			if (count == 4) return value;
			value.memoryPercent = reader.readNullableFloat64();
		}
		return value;
	}
}
