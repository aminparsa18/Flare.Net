// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricQueryResponse`. Can't
// carry `[GenerateTypeScript]` itself because its one member's type, `MetricSeries`, is
// hand-written - see `MetricSeries.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricSeries } from '$lib/memorypack/MetricSeries';

export class MetricQueryResponse {
	series: (MetricSeries | null)[] | null;
	treatedAsCounter: boolean;

	constructor() {
		this.series = null;
		this.treatedAsCounter = false;
	}

	static serialize(value: MetricQueryResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricQueryResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writer.writeArray(value.series, (writer, x) => MetricSeries.serializeCore(writer, x));
		writer.writeBoolean(value.treatedAsCounter);
	}

	static deserialize(buffer: ArrayBuffer): MetricQueryResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricQueryResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricQueryResponse();
		if (count == 2) {
			value.series = reader.readArray((reader) => MetricSeries.deserializeCore(reader));
			value.treatedAsCounter = reader.readBoolean();
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.series = reader.readArray((reader) => MetricSeries.deserializeCore(reader));
			if (count == 1) return value;
			value.treatedAsCounter = reader.readBoolean();
		}
		return value;
	}
}
