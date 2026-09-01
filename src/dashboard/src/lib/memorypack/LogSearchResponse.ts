// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogSearchRequest.cs`'s `LogSearchResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because its
// `Events` member's type, `LogEventDto`, has `DateTimeOffset` members - see
// `LogEventDto.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogEventDto } from '$lib/memorypack/LogEventDto';

export class LogSearchResponse {
	events: (LogEventDto | null)[] | null;
	nextCursor: string | null;

	constructor() {
		this.events = null;
		this.nextCursor = null;
	}

	static serialize(value: LogSearchResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogSearchResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writer.writeArray(value.events, (writer, x) => LogEventDto.serializeCore(writer, x));
		writer.writeString(value.nextCursor);
	}

	static deserialize(buffer: ArrayBuffer): LogSearchResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogSearchResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogSearchResponse();
		if (count == 2) {
			value.events = reader.readArray((reader) => LogEventDto.deserializeCore(reader));
			value.nextCursor = reader.readString();
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.events = reader.readArray((reader) => LogEventDto.deserializeCore(reader));
			if (count == 1) return value;
			value.nextCursor = reader.readString();
			if (count == 2) return value;
		}
		return value;
	}
}
