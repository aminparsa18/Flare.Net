// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanAttributeValuesRequest.cs`'s
// `SpanAttributeValuesResponse`. Can't carry `[GenerateTypeScript]` itself: its `Values`
// member is an `IReadOnlyList<SpanAttributeValueInfo>` - see
// `$lib/memorypack/LogAttributeKeysResponse.ts`'s header comment for why that alone blocks
// `[GenerateTypeScript]`. `SpanAttributeValueInfo` itself has no such member, so it's a real
// generated class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { SpanAttributeValueInfo } from '$lib/generated/memorypack/SpanAttributeValueInfo.js';

export class SpanAttributeValuesResponse {
	values: (SpanAttributeValueInfo | null)[] | null;

	constructor() {
		this.values = null;
	}

	static serialize(value: SpanAttributeValuesResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SpanAttributeValuesResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.values, (writer, x) => SpanAttributeValueInfo.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): SpanAttributeValuesResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SpanAttributeValuesResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SpanAttributeValuesResponse();
		if (count == 1) {
			value.values = reader.readArray((reader) => SpanAttributeValueInfo.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.values = reader.readArray((reader) => SpanAttributeValueInfo.deserializeCore(reader));
		}
		return value;
	}
}
