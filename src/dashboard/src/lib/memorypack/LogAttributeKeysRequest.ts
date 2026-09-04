// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogValueDistributionRequest.cs`'s
// `LogAttributeKeysRequest`. Can't carry `[GenerateTypeScript]` itself because its one
// member's type, `LogFilter`, is blocked - see `$lib/memorypack/LogFilter.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogFilter } from '$lib/memorypack/LogFilter';

export class LogAttributeKeysRequest {
	filter: LogFilter | null;

	constructor() {
		this.filter = null;
	}

	static serialize(value: LogAttributeKeysRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogAttributeKeysRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		LogFilter.serializeCore(writer, value.filter);
	}

	static deserialize(buffer: ArrayBuffer): LogAttributeKeysRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogAttributeKeysRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogAttributeKeysRequest();
		if (count == 1) {
			value.filter = LogFilter.deserializeCore(reader);
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = LogFilter.deserializeCore(reader);
		}
		return value;
	}
}
