// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricCatalogModels.cs`'s `MetricInspectSeries`, in
// declared order. Can't carry `[GenerateTypeScript]`: an `IReadOnlyDictionary` and two
// `IReadOnlyList<T>` members (see ADR-0016). `Attributes` decodes into a plain
// `Record<string, string>` - see `string-record.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricInspectSample } from '$lib/generated/memorypack/MetricInspectSample.js';
import { MetricInspectBucket } from '$lib/generated/memorypack/MetricInspectBucket.js';
import { readStringRecord, writeStringRecord, type StringRecord } from '$lib/memorypack/string-record';

export class MetricInspectSeries {
	serviceName: string | null;
	attributes: StringRecord;
	samples: (MetricInspectSample | null)[] | null;
	samplesTruncated: boolean;
	buckets: (MetricInspectBucket | null)[] | null;

	constructor() {
		this.serviceName = null;
		this.attributes = null;
		this.samples = null;
		this.samplesTruncated = false;
		this.buckets = null;
	}

	static serialize(value: MetricInspectSeries | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricInspectSeries | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writer.writeString(value.serviceName);
		writeStringRecord(writer, value.attributes);
		writer.writeArray(value.samples, (writer, x) => MetricInspectSample.serializeCore(writer, x));
		writer.writeBoolean(value.samplesTruncated);
		writer.writeArray(value.buckets, (writer, x) => MetricInspectBucket.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): MetricInspectSeries | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricInspectSeries | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricInspectSeries();
		if (count == 5) {
			value.serviceName = reader.readString();
			value.attributes = readStringRecord(reader);
			value.samples = reader.readArray((reader) => MetricInspectSample.deserializeCore(reader));
			value.samplesTruncated = reader.readBoolean();
			value.buckets = reader.readArray((reader) => MetricInspectBucket.deserializeCore(reader));
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.serviceName = reader.readString();
			if (count == 1) return value;
			value.attributes = readStringRecord(reader);
			if (count == 2) return value;
			value.samples = reader.readArray((reader) => MetricInspectSample.deserializeCore(reader));
			if (count == 3) return value;
			value.samplesTruncated = reader.readBoolean();
			if (count == 4) return value;
			value.buckets = reader.readArray((reader) => MetricInspectBucket.deserializeCore(reader));
		}
		return value;
	}
}
