// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ErrorModels.cs`'s `ExceptionOccurrencesRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `ExceptionFilter` (blocked - see `$lib/memorypack/ExceptionFilter.ts`'s header comment).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ExceptionFilter } from '$lib/memorypack/ExceptionFilter';

export class ExceptionOccurrencesRequest {
	filter: ExceptionFilter | null;
	exceptionType: string | null;
	exceptionMessage: string | null;

	constructor() {
		this.filter = null;
		this.exceptionType = null;
		this.exceptionMessage = null;
	}

	static serialize(value: ExceptionOccurrencesRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExceptionOccurrencesRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		ExceptionFilter.serializeCore(writer, value.filter);
		writer.writeString(value.exceptionType);
		writer.writeString(value.exceptionMessage);
	}

	static deserialize(buffer: ArrayBuffer): ExceptionOccurrencesRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExceptionOccurrencesRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExceptionOccurrencesRequest();
		if (count == 3) {
			value.filter = ExceptionFilter.deserializeCore(reader);
			value.exceptionType = reader.readString();
			value.exceptionMessage = reader.readString();
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.filter = ExceptionFilter.deserializeCore(reader);
			if (count == 1) return value;
			value.exceptionType = reader.readString();
			if (count == 2) return value;
			value.exceptionMessage = reader.readString();
			if (count == 3) return value;
		}
		return value;
	}
}
