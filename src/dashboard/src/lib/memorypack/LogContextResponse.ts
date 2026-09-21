// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogContextRequest.cs`'s `LogContextResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because its
// `Events` member's type, `LogEventDto`, has `DateTimeOffset` members - see
// `LogEventDto.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { LogEventDto } from '$lib/memorypack/LogEventDto';

export class LogContextResponse {
	events: (LogEventDto | null)[] | null;
	anchorEventId: string;
	hasMoreBefore: boolean;
	hasMoreAfter: boolean;

	constructor() {
		this.events = null;
		this.anchorEventId = '00000000-0000-0000-0000-000000000000';
		this.hasMoreBefore = false;
		this.hasMoreAfter = false;
	}

	static serialize(value: LogContextResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogContextResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeArray(value.events, (writer, x) => LogEventDto.serializeCore(writer, x));
		writer.writeGuid(value.anchorEventId);
		writer.writeBoolean(value.hasMoreBefore);
		writer.writeBoolean(value.hasMoreAfter);
	}

	static deserialize(buffer: ArrayBuffer): LogContextResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogContextResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogContextResponse();
		if (count == 4) {
			value.events = reader.readArray((reader) => LogEventDto.deserializeCore(reader));
			value.anchorEventId = reader.readGuid();
			value.hasMoreBefore = reader.readBoolean();
			value.hasMoreAfter = reader.readBoolean();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.events = reader.readArray((reader) => LogEventDto.deserializeCore(reader));
			if (count == 1) return value;
			value.anchorEventId = reader.readGuid();
			if (count == 2) return value;
			value.hasMoreBefore = reader.readBoolean();
			if (count == 3) return value;
			value.hasMoreAfter = reader.readBoolean();
			if (count == 4) return value;
		}
		return value;
	}
}
