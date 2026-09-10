// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ErrorModels.cs`'s `ExceptionOccurrencesResponse`
// field-for-field. Can't carry `[GenerateTypeScript]` itself because an
// `IReadOnlyList<ExceptionOccurrence>` member blocks the generator - same
// `ExceptionGroupsResponse`/`ServiceCallBreakdownResponse` precedent.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ExceptionOccurrence } from '$lib/memorypack/ExceptionOccurrence';

export class ExceptionOccurrencesResponse {
	exceptionType: string | null;
	exceptionMessage: string | null;
	occurrences: (ExceptionOccurrence | null)[] | null;

	constructor() {
		this.exceptionType = null;
		this.exceptionMessage = null;
		this.occurrences = null;
	}

	static serialize(value: ExceptionOccurrencesResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExceptionOccurrencesResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeString(value.exceptionType);
		writer.writeString(value.exceptionMessage);
		writer.writeArray(value.occurrences, (writer, x) => ExceptionOccurrence.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ExceptionOccurrencesResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExceptionOccurrencesResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExceptionOccurrencesResponse();
		if (count == 3) {
			value.exceptionType = reader.readString();
			value.exceptionMessage = reader.readString();
			value.occurrences = reader.readArray((reader) => ExceptionOccurrence.deserializeCore(reader));
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.exceptionType = reader.readString();
			if (count == 1) return value;
			value.exceptionMessage = reader.readString();
			if (count == 2) return value;
			value.occurrences = reader.readArray((reader) => ExceptionOccurrence.deserializeCore(reader));
			if (count == 3) return value;
		}
		return value;
	}
}
