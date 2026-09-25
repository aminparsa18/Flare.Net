// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/HostInventoryModels.cs`'s `HostMetricsPoint`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `BucketStart` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class HostMetricsPoint {
	bucketStart: Date;
	cpuPercent: number | null;
	memoryPercent: number | null;
	diskPercent: number | null;
	loadAverage15m: number | null;

	constructor() {
		this.bucketStart = new Date(0);
		this.cpuPercent = null;
		this.memoryPercent = null;
		this.diskPercent = null;
		this.loadAverage15m = null;
	}

	static serialize(value: HostMetricsPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: HostMetricsPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writeDateTimeOffset(writer, value.bucketStart);
		writer.writeNullableFloat64(value.cpuPercent);
		writer.writeNullableFloat64(value.memoryPercent);
		writer.writeNullableFloat64(value.diskPercent);
		writer.writeNullableFloat64(value.loadAverage15m);
	}

	static deserialize(buffer: ArrayBuffer): HostMetricsPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): HostMetricsPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new HostMetricsPoint();
		if (count == 5) {
			value.bucketStart = readDateTimeOffset(reader);
			value.cpuPercent = reader.readNullableFloat64();
			value.memoryPercent = reader.readNullableFloat64();
			value.diskPercent = reader.readNullableFloat64();
			value.loadAverage15m = reader.readNullableFloat64();
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bucketStart = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.cpuPercent = reader.readNullableFloat64();
			if (count == 2) return value;
			value.memoryPercent = reader.readNullableFloat64();
			if (count == 3) return value;
			value.diskPercent = reader.readNullableFloat64();
			if (count == 4) return value;
			value.loadAverage15m = reader.readNullableFloat64();
		}
		return value;
	}
}
