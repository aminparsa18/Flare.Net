// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesWorkloadModels.cs`'s `KubernetesWorkloadMetricsPoint`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself because it has a `DateTimeOffset` member - see `$lib/memorypack/date-time-offset.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class KubernetesWorkloadMetricsPoint {
	bucketStart: Date;
	desired: number | null;
	ready: number | null;
	current: number | null;
	misscheduled: number | null;
	active: number | null;
	succeeded: number | null;
	failed: number | null;
	cpuCores: number | null;
	memoryWorkingSetBytes: number | null;

	constructor() {
		this.bucketStart = new Date(0);
		this.desired = null;
		this.ready = null;
		this.current = null;
		this.misscheduled = null;
		this.active = null;
		this.succeeded = null;
		this.failed = null;
		this.cpuCores = null;
		this.memoryWorkingSetBytes = null;
	}

	static serialize(value: KubernetesWorkloadMetricsPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesWorkloadMetricsPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(10);
		writeDateTimeOffset(writer, value.bucketStart);
		writer.writeNullableInt32(value.desired);
		writer.writeNullableInt32(value.ready);
		writer.writeNullableInt32(value.current);
		writer.writeNullableInt32(value.misscheduled);
		writer.writeNullableInt32(value.active);
		writer.writeNullableInt32(value.succeeded);
		writer.writeNullableInt32(value.failed);
		writer.writeNullableFloat64(value.cpuCores);
		writer.writeNullableFloat64(value.memoryWorkingSetBytes);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesWorkloadMetricsPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesWorkloadMetricsPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesWorkloadMetricsPoint();
		if (count == 10) {
			value.bucketStart = readDateTimeOffset(reader);
			value.desired = reader.readNullableInt32();
			value.ready = reader.readNullableInt32();
			value.current = reader.readNullableInt32();
			value.misscheduled = reader.readNullableInt32();
			value.active = reader.readNullableInt32();
			value.succeeded = reader.readNullableInt32();
			value.failed = reader.readNullableInt32();
			value.cpuCores = reader.readNullableFloat64();
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
		} else if (count > 10) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bucketStart = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.desired = reader.readNullableInt32();
			if (count == 2) return value;
			value.ready = reader.readNullableInt32();
			if (count == 3) return value;
			value.current = reader.readNullableInt32();
			if (count == 4) return value;
			value.misscheduled = reader.readNullableInt32();
			if (count == 5) return value;
			value.active = reader.readNullableInt32();
			if (count == 6) return value;
			value.succeeded = reader.readNullableInt32();
			if (count == 7) return value;
			value.failed = reader.readNullableInt32();
			if (count == 8) return value;
			value.cpuCores = reader.readNullableFloat64();
			if (count == 9) return value;
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
		}
		return value;
	}
}
