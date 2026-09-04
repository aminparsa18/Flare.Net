// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/MetricModels.cs`'s `MetricAttributeKeysResponse`.
// Can't carry `[GenerateTypeScript]` itself: its `Keys` member is an
// `IReadOnlyList<MetricAttributeKeyInfo>` - see `PipelineServiceBreakdown.ts`'s header
// comment for why that alone blocks `[GenerateTypeScript]`. `MetricAttributeKeyInfo` itself
// has no such member, so it's a real generated class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { MetricAttributeKeyInfo } from '$lib/generated/memorypack/MetricAttributeKeyInfo.js';

export class MetricAttributeKeysResponse {
	keys: (MetricAttributeKeyInfo | null)[] | null;

	constructor() {
		this.keys = null;
	}

	static serialize(value: MetricAttributeKeysResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: MetricAttributeKeysResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.keys, (writer, x) => MetricAttributeKeyInfo.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): MetricAttributeKeysResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): MetricAttributeKeysResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new MetricAttributeKeysResponse();
		if (count == 1) {
			value.keys = reader.readArray((reader) => MetricAttributeKeyInfo.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.keys = reader.readArray((reader) => MetricAttributeKeyInfo.deserializeCore(reader));
		}
		return value;
	}
}
