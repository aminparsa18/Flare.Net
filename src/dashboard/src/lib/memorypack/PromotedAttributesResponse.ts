// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PromotedAttributeModels.cs`'s
// `PromotedAttributesResponse` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself because of its `IReadOnlyList<PromotedAttributeInfo>`
// member - see `PipelineServiceBreakdown.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { PromotedAttributeInfo } from '$lib/generated/memorypack/PromotedAttributeInfo.js';

export class PromotedAttributesResponse {
	attributes: (PromotedAttributeInfo | null)[] | null;
	maxPromotedAttributes: number;

	constructor() {
		this.attributes = null;
		this.maxPromotedAttributes = 0;
	}

	static serialize(value: PromotedAttributesResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PromotedAttributesResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writer.writeArray(value.attributes, (writer, x) => PromotedAttributeInfo.serializeCore(writer, x));
		writer.writeInt32(value.maxPromotedAttributes);
	}

	static deserialize(buffer: ArrayBuffer): PromotedAttributesResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PromotedAttributesResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PromotedAttributesResponse();
		if (count == 2) {
			value.attributes = reader.readArray((reader) => PromotedAttributeInfo.deserializeCore(reader));
			value.maxPromotedAttributes = reader.readInt32();
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.attributes = reader.readArray((reader) => PromotedAttributeInfo.deserializeCore(reader));
			if (count == 1) return value;
		}
		return value;
	}
}
