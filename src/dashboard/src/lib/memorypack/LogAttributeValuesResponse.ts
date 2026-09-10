// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogAttributeValuesRequest.cs`'s
// `LogAttributeValuesResponse`. Can't carry `[GenerateTypeScript]` itself: its `Values`
// member is an `IReadOnlyList<LogAttributeValueInfo>` - see
// `$lib/memorypack/LogAttributeKeysResponse.ts`'s header comment for why that alone blocks
// `[GenerateTypeScript]`. `LogAttributeValueInfo` itself has no such member, so it's a real
// generated class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogAttributeValueInfo } from '$lib/generated/memorypack/LogAttributeValueInfo.js';

export class LogAttributeValuesResponse {
	values: (LogAttributeValueInfo | null)[] | null;

	constructor() {
		this.values = null;
	}

	static serialize(value: LogAttributeValuesResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogAttributeValuesResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.values, (writer, x) => LogAttributeValueInfo.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogAttributeValuesResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogAttributeValuesResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogAttributeValuesResponse();
		if (count == 1) {
			value.values = reader.readArray((reader) => LogAttributeValueInfo.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.values = reader.readArray((reader) => LogAttributeValueInfo.deserializeCore(reader));
		}
		return value;
	}
}
