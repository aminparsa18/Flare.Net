// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricCatalogModels.cs`'s
// `MetricCatalogAttributeInfo`, in declared order. Can't carry `[GenerateTypeScript]`
// itself: `SampleValues` is an `IReadOnlyList<string>` (see ADR-0016).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class MetricCatalogAttributeInfo {
	key: string;
	distinctValueCount: bigint;
	sampleCount: bigint;
	sampleValues: (string | null)[] | null;

	constructor() {
		this.key = '';
		this.distinctValueCount = 0n;
		this.sampleCount = 0n;
		this.sampleValues = null;
	}

	static serialize(value: MetricCatalogAttributeInfo | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricCatalogAttributeInfo | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.key);
		writer.writeInt64(value.distinctValueCount);
		writer.writeInt64(value.sampleCount);
		writer.writeArray(value.sampleValues, (writer, x) => writer.writeString(x));
	}

	static deserialize(buffer: ArrayBuffer): MetricCatalogAttributeInfo | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricCatalogAttributeInfo | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricCatalogAttributeInfo();
		if (count == 4) {
			value.key = reader.readString() ?? '';
			value.distinctValueCount = reader.readInt64();
			value.sampleCount = reader.readInt64();
			value.sampleValues = reader.readArray((reader) => reader.readString());
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.key = reader.readString() ?? '';
			if (count == 1) return value;
			value.distinctValueCount = reader.readInt64();
			if (count == 2) return value;
			value.sampleCount = reader.readInt64();
			if (count == 3) return value;
			value.sampleValues = reader.readArray((reader) => reader.readString());
		}
		return value;
	}
}
