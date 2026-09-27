// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ErrorModels.cs`'s `ExceptionFacetValuesResponse`.
// Can't carry `[GenerateTypeScript]` itself (its `Values` member is a list - same as
// `SpanAttributeValuesResponse.ts`); `ExceptionFacetValue` is a real generated class.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ExceptionFacetValue } from '$lib/generated/memorypack/ExceptionFacetValue.js';

export class ExceptionFacetValuesResponse {
	values: (ExceptionFacetValue | null)[] | null;

	constructor() {
		this.values = null;
	}

	static serialize(value: ExceptionFacetValuesResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExceptionFacetValuesResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.values, (writer, x) => ExceptionFacetValue.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ExceptionFacetValuesResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExceptionFacetValuesResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExceptionFacetValuesResponse();
		if (count == 1) {
			value.values = reader.readArray((reader) => ExceptionFacetValue.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.values = reader.readArray((reader) => ExceptionFacetValue.deserializeCore(reader));
		}
		return value;
	}
}
