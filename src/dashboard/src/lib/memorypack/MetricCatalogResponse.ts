// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricCatalogModels.cs`'s `MetricCatalogResponse`,
// in declared order. Can't carry `[GenerateTypeScript]` itself: `Metrics` is an
// `IReadOnlyList<T>` (see ADR-0016). `MetricCatalogEntry` has no such member, so it's a real
// generated class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricCatalogEntry } from '$lib/generated/memorypack/MetricCatalogEntry.js';

export class MetricCatalogResponse {
	windowMinutes: number;
	metrics: (MetricCatalogEntry | null)[] | null;
	truncated: boolean;

	constructor() {
		this.windowMinutes = 0;
		this.metrics = null;
		this.truncated = false;
	}

	static serialize(value: MetricCatalogResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricCatalogResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.metrics, (writer, x) => MetricCatalogEntry.serializeCore(writer, x));
		writer.writeBoolean(value.truncated);
	}

	static deserialize(buffer: ArrayBuffer): MetricCatalogResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricCatalogResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricCatalogResponse();
		if (count == 3) {
			value.windowMinutes = reader.readInt32();
			value.metrics = reader.readArray((reader) => MetricCatalogEntry.deserializeCore(reader));
			value.truncated = reader.readBoolean();
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 1) return value;
			value.metrics = reader.readArray((reader) => MetricCatalogEntry.deserializeCore(reader));
			if (count == 2) return value;
			value.truncated = reader.readBoolean();
		}
		return value;
	}
}
