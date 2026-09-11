// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanDto.cs`'s `SpanLinkDto` field-for-field, in
// declared order.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readStringRecord, writeStringRecord, type StringRecord } from '$lib/memorypack/string-record';

export class SpanLinkDto {
	traceId: string | null;
	spanId: string | null;
	traceState: string | null;
	attributes: StringRecord;

	constructor() {
		this.traceId = null;
		this.spanId = null;
		this.traceState = null;
		this.attributes = null;
	}

	static serialize(value: SpanLinkDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SpanLinkDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.traceId);
		writer.writeString(value.spanId);
		writer.writeString(value.traceState);
		writeStringRecord(writer, value.attributes);
	}

	static serializeArray(value: (SpanLinkDto | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (SpanLinkDto | null)[] | null): void {
		writer.writeArray(value, (writer, x) => SpanLinkDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): SpanLinkDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SpanLinkDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SpanLinkDto();
		if (count == 4) {
			value.traceId = reader.readString();
			value.spanId = reader.readString();
			value.traceState = reader.readString();
			value.attributes = readStringRecord(reader);
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.traceId = reader.readString();
			if (count == 1) return value;
			value.spanId = reader.readString();
			if (count == 2) return value;
			value.traceState = reader.readString();
			if (count == 3) return value;
			value.attributes = readStringRecord(reader);
			if (count == 4) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (SpanLinkDto | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (SpanLinkDto | null)[] | null {
		return reader.readArray((reader) => SpanLinkDto.deserializeCore(reader));
	}
}
