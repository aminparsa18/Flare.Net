// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ErrorModels.cs`'s `ExceptionFacetValuesRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `ExceptionFilter` (`DateTimeOffset?` members). `field` is a raw MemoryPack numeric
// ordinal, converted at the `errors-api.ts` boundary - same convention as
// `SpanAttributeValuesRequest.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ExceptionFilter } from '$lib/memorypack/ExceptionFilter';

export class ExceptionFacetValuesRequest {
	filter: ExceptionFilter | null;
	field: number;
	key: string | null;
	limit: number | null;

	constructor() {
		this.filter = null;
		this.field = 0;
		this.key = null;
		this.limit = null;
	}

	static serialize(value: ExceptionFacetValuesRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExceptionFacetValuesRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		ExceptionFilter.serializeCore(writer, value.filter);
		writer.writeInt32(value.field);
		writer.writeString(value.key);
		writer.writeNullableInt32(value.limit);
	}

	static deserialize(buffer: ArrayBuffer): ExceptionFacetValuesRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExceptionFacetValuesRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExceptionFacetValuesRequest();
		if (count == 4) {
			value.filter = ExceptionFilter.deserializeCore(reader);
			value.field = reader.readInt32();
			value.key = reader.readString();
			value.limit = reader.readNullableInt32();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = ExceptionFilter.deserializeCore(reader);
			if (count == 1) return value;
			value.field = reader.readInt32();
			if (count == 2) return value;
			value.key = reader.readString();
			if (count == 3) return value;
			value.limit = reader.readNullableInt32();
		}
		return value;
	}
}
