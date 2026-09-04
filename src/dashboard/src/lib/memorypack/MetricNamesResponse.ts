// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricNamesResponse`. Can't
// carry `[GenerateTypeScript]` itself: its `Metrics` member is an
// `IReadOnlyList<MetricNameInfo>` - see `PipelineServiceBreakdown.ts`'s header comment for
// why that alone blocks `[GenerateTypeScript]` (confirmed by actually attaching the
// attribute here and hitting `MEMPACK031` before switching to hand-writing it).
// `MetricNameInfo` itself has no such member, so it's a real generated class, reused here
// directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricNameInfo } from '$lib/generated/memorypack/MetricNameInfo.js';

export class MetricNamesResponse {
	metrics: (MetricNameInfo | null)[] | null;

	constructor() {
		this.metrics = null;
	}

	static serialize(value: MetricNamesResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricNamesResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.metrics, (writer, x) => MetricNameInfo.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): MetricNamesResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricNamesResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricNamesResponse();
		if (count == 1) {
			value.metrics = reader.readArray((reader) => MetricNameInfo.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.metrics = reader.readArray((reader) => MetricNameInfo.deserializeCore(reader));
		}
		return value;
	}
}
