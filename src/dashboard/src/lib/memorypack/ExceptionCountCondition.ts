// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `ExceptionCountCondition`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// nests `ExceptionFilter` (blocked - see `$lib/memorypack/ExceptionFilter.ts`'s header
// comment, itself nullable-`DateTimeOffset`-blocked).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ExceptionFilter } from '$lib/memorypack/ExceptionFilter';

export class ExceptionCountCondition {
	exceptionType: string | null;
	exceptionMessage: string | null;
	filter: ExceptionFilter | null;

	constructor() {
		this.exceptionType = null;
		this.exceptionMessage = null;
		this.filter = null;
	}

	static serialize(value: ExceptionCountCondition | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExceptionCountCondition | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeString(value.exceptionType);
		writer.writeString(value.exceptionMessage);
		ExceptionFilter.serializeCore(writer, value.filter);
	}

	static deserialize(buffer: ArrayBuffer): ExceptionCountCondition | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExceptionCountCondition | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExceptionCountCondition();
		if (count == 3) {
			value.exceptionType = reader.readString();
			value.exceptionMessage = reader.readString();
			value.filter = ExceptionFilter.deserializeCore(reader);
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.exceptionType = reader.readString();
			if (count == 1) return value;
			value.exceptionMessage = reader.readString();
			if (count == 2) return value;
			value.filter = ExceptionFilter.deserializeCore(reader);
			if (count == 3) return value;
		}
		return value;
	}
}
