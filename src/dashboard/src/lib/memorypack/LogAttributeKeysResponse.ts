// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogValueDistributionRequest.cs`'s
// `LogAttributeKeysResponse`. Can't carry `[GenerateTypeScript]` itself: its `Keys` member
// is an `IReadOnlyList<LogAttributeKeyInfo>` - see `PipelineServiceBreakdown.ts`'s header
// comment for why that alone blocks `[GenerateTypeScript]` (confirmed by actually attaching
// the attribute here and hitting `MEMPACK031` before switching to hand-writing it).
// `LogAttributeKeyInfo` itself has no such member, so it's a real generated class, reused
// here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogAttributeKeyInfo } from '$lib/generated/memorypack/LogAttributeKeyInfo.js';

export class LogAttributeKeysResponse {
	keys: (LogAttributeKeyInfo | null)[] | null;

	constructor() {
		this.keys = null;
	}

	static serialize(value: LogAttributeKeysResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogAttributeKeysResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.keys, (writer, x) => LogAttributeKeyInfo.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogAttributeKeysResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogAttributeKeysResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogAttributeKeysResponse();
		if (count == 1) {
			value.keys = reader.readArray((reader) => LogAttributeKeyInfo.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.keys = reader.readArray((reader) => LogAttributeKeyInfo.deserializeCore(reader));
		}
		return value;
	}
}
