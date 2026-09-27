// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricCatalogModels.cs`'s
// `MetricCatalogInspectResponse`, in declared order. Can't carry `[GenerateTypeScript]`: two
// `IReadOnlyList<T>` members (see ADR-0016).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricInspectBucket } from '$lib/generated/memorypack/MetricInspectBucket.js';
import { MetricInspectSeries } from '$lib/memorypack/MetricInspectSeries';

export class MetricCatalogInspectResponse {
	metricName: string | null;
	type: number;
	windowMinutes: number;
	bucketWidthSeconds: number;
	totalSeriesCount: bigint;
	series: (MetricInspectSeries | null)[] | null;
	merged: (MetricInspectBucket | null)[] | null;

	constructor() {
		this.metricName = null;
		this.type = 0;
		this.windowMinutes = 0;
		this.bucketWidthSeconds = 0;
		this.totalSeriesCount = 0n;
		this.series = null;
		this.merged = null;
	}

	static serialize(value: MetricCatalogInspectResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricCatalogInspectResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(7);
		writer.writeString(value.metricName);
		writer.writeInt32(value.type);
		writer.writeInt32(value.windowMinutes);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeInt64(value.totalSeriesCount);
		writer.writeArray(value.series, (writer, x) => MetricInspectSeries.serializeCore(writer, x));
		writer.writeArray(value.merged, (writer, x) => MetricInspectBucket.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): MetricCatalogInspectResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricCatalogInspectResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricCatalogInspectResponse();
		if (count == 7) {
			value.metricName = reader.readString();
			value.type = reader.readInt32();
			value.windowMinutes = reader.readInt32();
			value.bucketWidthSeconds = reader.readInt32();
			value.totalSeriesCount = reader.readInt64();
			value.series = reader.readArray((reader) => MetricInspectSeries.deserializeCore(reader));
			value.merged = reader.readArray((reader) => MetricInspectBucket.deserializeCore(reader));
		} else if (count > 7) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.metricName = reader.readString();
			if (count == 1) return value;
			value.type = reader.readInt32();
			if (count == 2) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 3) return value;
			value.bucketWidthSeconds = reader.readInt32();
			if (count == 4) return value;
			value.totalSeriesCount = reader.readInt64();
			if (count == 5) return value;
			value.series = reader.readArray((reader) => MetricInspectSeries.deserializeCore(reader));
			if (count == 6) return value;
			value.merged = reader.readArray((reader) => MetricInspectBucket.deserializeCore(reader));
		}
		return value;
	}
}
