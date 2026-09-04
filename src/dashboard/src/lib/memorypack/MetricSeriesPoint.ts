// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricSeriesPoint`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `BucketStart` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class MetricSeriesPoint {
	bucketStart: Date;
	value: number | null;
	count: bigint | null;
	sum: number | null;
	p50: number | null;
	p75: number | null;
	p90: number | null;
	p95: number | null;
	p99: number | null;
	maxApprox: number | null;

	constructor() {
		this.bucketStart = new Date(0);
		this.value = null;
		this.count = null;
		this.sum = null;
		this.p50 = null;
		this.p75 = null;
		this.p90 = null;
		this.p95 = null;
		this.p99 = null;
		this.maxApprox = null;
	}

	static serialize(value: MetricSeriesPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricSeriesPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(10);
		writeDateTimeOffset(writer, value.bucketStart);
		writer.writeNullableFloat64(value.value);
		writer.writeNullableInt64(value.count);
		writer.writeNullableFloat64(value.sum);
		writer.writeNullableFloat64(value.p50);
		writer.writeNullableFloat64(value.p75);
		writer.writeNullableFloat64(value.p90);
		writer.writeNullableFloat64(value.p95);
		writer.writeNullableFloat64(value.p99);
		writer.writeNullableFloat64(value.maxApprox);
	}

	static serializeArray(value: (MetricSeriesPoint | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (MetricSeriesPoint | null)[] | null): void {
		writer.writeArray(value, (writer, x) => MetricSeriesPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): MetricSeriesPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricSeriesPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricSeriesPoint();
		if (count == 10) {
			value.bucketStart = readDateTimeOffset(reader);
			value.value = reader.readNullableFloat64();
			value.count = reader.readNullableInt64();
			value.sum = reader.readNullableFloat64();
			value.p50 = reader.readNullableFloat64();
			value.p75 = reader.readNullableFloat64();
			value.p90 = reader.readNullableFloat64();
			value.p95 = reader.readNullableFloat64();
			value.p99 = reader.readNullableFloat64();
			value.maxApprox = reader.readNullableFloat64();
		} else if (count > 10) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bucketStart = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.value = reader.readNullableFloat64();
			if (count == 2) return value;
			value.count = reader.readNullableInt64();
			if (count == 3) return value;
			value.sum = reader.readNullableFloat64();
			if (count == 4) return value;
			value.p50 = reader.readNullableFloat64();
			if (count == 5) return value;
			value.p75 = reader.readNullableFloat64();
			if (count == 6) return value;
			value.p90 = reader.readNullableFloat64();
			if (count == 7) return value;
			value.p95 = reader.readNullableFloat64();
			if (count == 8) return value;
			value.p99 = reader.readNullableFloat64();
			if (count == 9) return value;
			value.maxApprox = reader.readNullableFloat64();
			if (count == 10) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (MetricSeriesPoint | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (MetricSeriesPoint | null)[] | null {
		return reader.readArray((reader) => MetricSeriesPoint.deserializeCore(reader));
	}
}
