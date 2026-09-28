// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesWorkloadModels.cs`'s `KubernetesVolumeMetricsPoint`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself because it has a `DateTimeOffset` member - see `$lib/memorypack/date-time-offset.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class KubernetesVolumeMetricsPoint {
	bucketStart: Date;
	usedBytes: number | null;
	usedPercent: number | null;
	inodesUsedPercent: number | null;

	constructor() {
		this.bucketStart = new Date(0);
		this.usedBytes = null;
		this.usedPercent = null;
		this.inodesUsedPercent = null;
	}

	static serialize(value: KubernetesVolumeMetricsPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesVolumeMetricsPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writeDateTimeOffset(writer, value.bucketStart);
		writer.writeNullableFloat64(value.usedBytes);
		writer.writeNullableFloat64(value.usedPercent);
		writer.writeNullableFloat64(value.inodesUsedPercent);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesVolumeMetricsPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesVolumeMetricsPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesVolumeMetricsPoint();
		if (count == 4) {
			value.bucketStart = readDateTimeOffset(reader);
			value.usedBytes = reader.readNullableFloat64();
			value.usedPercent = reader.readNullableFloat64();
			value.inodesUsedPercent = reader.readNullableFloat64();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bucketStart = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.usedBytes = reader.readNullableFloat64();
			if (count == 2) return value;
			value.usedPercent = reader.readNullableFloat64();
			if (count == 3) return value;
			value.inodesUsedPercent = reader.readNullableFloat64();
		}
		return value;
	}
}
