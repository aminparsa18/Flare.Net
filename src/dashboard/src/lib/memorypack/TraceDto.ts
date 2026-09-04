// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanSearchRequest.cs`'s `TraceDto` field-for-field,
// in declared order. Can't carry `[GenerateTypeScript]` itself because its `Spans` member's
// type, `SpanDto`, has `DateTimeOffset` members - see `SpanDto.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { SpanDto } from '$lib/memorypack/SpanDto';

export class TraceDto {
	traceId: string | null;
	spans: (SpanDto | null)[] | null;

	constructor() {
		this.traceId = null;
		this.spans = null;
	}

	static serialize(value: TraceDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: TraceDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writer.writeString(value.traceId);
		writer.writeArray(value.spans, (writer, x) => SpanDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): TraceDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): TraceDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new TraceDto();
		if (count == 2) {
			value.traceId = reader.readString();
			value.spans = reader.readArray((reader) => SpanDto.deserializeCore(reader));
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.traceId = reader.readString();
			if (count == 1) return value;
			value.spans = reader.readArray((reader) => SpanDto.deserializeCore(reader));
			if (count == 2) return value;
		}
		return value;
	}
}
