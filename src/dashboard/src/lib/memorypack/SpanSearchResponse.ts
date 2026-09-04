// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanSearchRequest.cs`'s `SpanSearchResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because its
// `Spans` member's type, `SpanDto`, has `DateTimeOffset` members - see `SpanDto.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { SpanDto } from '$lib/memorypack/SpanDto';

export class SpanSearchResponse {
	spans: (SpanDto | null)[] | null;
	nextCursor: string | null;

	constructor() {
		this.spans = null;
		this.nextCursor = null;
	}

	static serialize(value: SpanSearchResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SpanSearchResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writer.writeArray(value.spans, (writer, x) => SpanDto.serializeCore(writer, x));
		writer.writeString(value.nextCursor);
	}

	static deserialize(buffer: ArrayBuffer): SpanSearchResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SpanSearchResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SpanSearchResponse();
		if (count == 2) {
			value.spans = reader.readArray((reader) => SpanDto.deserializeCore(reader));
			value.nextCursor = reader.readString();
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.spans = reader.readArray((reader) => SpanDto.deserializeCore(reader));
			if (count == 1) return value;
			value.nextCursor = reader.readString();
			if (count == 2) return value;
		}
		return value;
	}
}
