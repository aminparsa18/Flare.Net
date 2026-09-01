// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogValueDistributionRequest.cs`'s
// `LogValueDistributionRequest` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself because it nests `LogFilter` (blocked - see
// `$lib/memorypack/LogFilter.ts`'s header comment).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogFilter } from '$lib/memorypack/LogFilter';

export class LogValueDistributionRequest {
	filter: LogFilter | null;
	attributeKey: string | null;
	sampleSize: number;

	constructor() {
		this.filter = null;
		this.attributeKey = null;
		this.sampleSize = 0;
	}

	static serialize(value: LogValueDistributionRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogValueDistributionRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		LogFilter.serializeCore(writer, value.filter);
		writer.writeString(value.attributeKey);
		writer.writeInt32(value.sampleSize);
	}

	static deserialize(buffer: ArrayBuffer): LogValueDistributionRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogValueDistributionRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogValueDistributionRequest();
		if (count == 3) {
			value.filter = LogFilter.deserializeCore(reader);
			value.attributeKey = reader.readString();
			value.sampleSize = reader.readInt32();
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = LogFilter.deserializeCore(reader);
			if (count == 1) return value;
			value.attributeKey = reader.readString();
			if (count == 2) return value;
			value.sampleSize = reader.readInt32();
			if (count == 3) return value;
		}
		return value;
	}
}
