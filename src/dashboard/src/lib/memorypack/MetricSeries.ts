// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricSeries` field-for-field,
// in declared order. Can't carry `[GenerateTypeScript]` itself because its `Points` member's
// type, `MetricSeriesPoint`, has a `DateTimeOffset` member - see `MetricSeriesPoint.ts`'s
// header comment. `Attributes` (`IReadOnlyDictionary<string, string>`) decodes into a plain
// `Record<string, string>` - see `$lib/memorypack/string-record.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricSeriesPoint } from '$lib/memorypack/MetricSeriesPoint';
import { readStringRecord, writeStringRecord, type StringRecord } from '$lib/memorypack/string-record';

export class MetricSeries {
	serviceName: string | null;
	attributes: StringRecord;
	points: (MetricSeriesPoint | null)[] | null;

	constructor() {
		this.serviceName = null;
		this.attributes = null;
		this.points = null;
	}

	static serialize(value: MetricSeries | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricSeries | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeString(value.serviceName);
		writeStringRecord(writer, value.attributes);
		writer.writeArray(value.points, (writer, x) => MetricSeriesPoint.serializeCore(writer, x));
	}

	static serializeArray(value: (MetricSeries | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (MetricSeries | null)[] | null): void {
		writer.writeArray(value, (writer, x) => MetricSeries.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): MetricSeries | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricSeries | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricSeries();
		if (count == 3) {
			value.serviceName = reader.readString();
			value.attributes = readStringRecord(reader);
			value.points = reader.readArray((reader) => MetricSeriesPoint.deserializeCore(reader));
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.serviceName = reader.readString();
			if (count == 1) return value;
			value.attributes = readStringRecord(reader);
			if (count == 2) return value;
			value.points = reader.readArray((reader) => MetricSeriesPoint.deserializeCore(reader));
			if (count == 3) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (MetricSeries | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (MetricSeries | null)[] | null {
		return reader.readArray((reader) => MetricSeries.deserializeCore(reader));
	}
}
