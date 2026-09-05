// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SpanDto.cs`'s `SpanDto` field-for-field, in
// declared order. Can't carry `[GenerateTypeScript]` itself because `StartTime`/`EndTime`/
// `IngestedAt` are `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { SpanEventDto } from '$lib/memorypack/SpanEventDto';
import { readStringRecord, writeStringRecord, type StringRecord } from '$lib/memorypack/string-record';

export class SpanDto {
	traceId: string | null;
	spanId: string | null;
	parentSpanId: string | null;
	traceState: string | null;
	name: string | null;
	kind: number;
	startTime: Date;
	endTime: Date;
	ingestedAt: Date;
	durationNano: bigint;
	statusCode: string | null;
	statusMessage: string | null;
	serviceName: string | null;
	resourceSchemaUrl: string | null;
	resourceAttributes: StringRecord;
	scopeSchemaUrl: string | null;
	scopeName: string | null;
	scopeVersion: string | null;
	scopeAttributes: StringRecord;
	spanAttributes: StringRecord;
	events: (SpanEventDto | null)[] | null;
	spanCount: bigint | null;

	constructor() {
		this.traceId = null;
		this.spanId = null;
		this.parentSpanId = null;
		this.traceState = null;
		this.name = null;
		this.kind = 0;
		this.startTime = new Date(0);
		this.endTime = new Date(0);
		this.ingestedAt = new Date(0);
		this.durationNano = 0n;
		this.statusCode = null;
		this.statusMessage = null;
		this.serviceName = null;
		this.resourceSchemaUrl = null;
		this.resourceAttributes = null;
		this.scopeSchemaUrl = null;
		this.scopeName = null;
		this.scopeVersion = null;
		this.scopeAttributes = null;
		this.spanAttributes = null;
		this.events = null;
		this.spanCount = null;
	}

	static serialize(value: SpanDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SpanDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(22);
		writer.writeString(value.traceId);
		writer.writeString(value.spanId);
		writer.writeString(value.parentSpanId);
		writer.writeString(value.traceState);
		writer.writeString(value.name);
		writer.writeUint8(value.kind);
		writeDateTimeOffset(writer, value.startTime);
		writeDateTimeOffset(writer, value.endTime);
		writeDateTimeOffset(writer, value.ingestedAt);
		writer.writeUint64(value.durationNano);
		writer.writeString(value.statusCode);
		writer.writeString(value.statusMessage);
		writer.writeString(value.serviceName);
		writer.writeString(value.resourceSchemaUrl);
		writeStringRecord(writer, value.resourceAttributes);
		writer.writeString(value.scopeSchemaUrl);
		writer.writeString(value.scopeName);
		writer.writeString(value.scopeVersion);
		writeStringRecord(writer, value.scopeAttributes);
		writeStringRecord(writer, value.spanAttributes);
		writer.writeArray(value.events, (writer, x) => SpanEventDto.serializeCore(writer, x));
		writer.writeNullableUint64(value.spanCount);
	}

	static serializeArray(value: (SpanDto | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (SpanDto | null)[] | null): void {
		writer.writeArray(value, (writer, x) => SpanDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): SpanDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SpanDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SpanDto();
		if (count == 22) {
			value.traceId = reader.readString();
			value.spanId = reader.readString();
			value.parentSpanId = reader.readString();
			value.traceState = reader.readString();
			value.name = reader.readString();
			value.kind = reader.readUint8();
			value.startTime = readDateTimeOffset(reader);
			value.endTime = readDateTimeOffset(reader);
			value.ingestedAt = readDateTimeOffset(reader);
			value.durationNano = reader.readUint64();
			value.statusCode = reader.readString();
			value.statusMessage = reader.readString();
			value.serviceName = reader.readString();
			value.resourceSchemaUrl = reader.readString();
			value.resourceAttributes = readStringRecord(reader);
			value.scopeSchemaUrl = reader.readString();
			value.scopeName = reader.readString();
			value.scopeVersion = reader.readString();
			value.scopeAttributes = readStringRecord(reader);
			value.spanAttributes = readStringRecord(reader);
			value.events = reader.readArray((reader) => SpanEventDto.deserializeCore(reader));
			value.spanCount = reader.readNullableUint64();
		} else if (count > 22) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.traceId = reader.readString();
			if (count == 1) return value;
			value.spanId = reader.readString();
			if (count == 2) return value;
			value.parentSpanId = reader.readString();
			if (count == 3) return value;
			value.traceState = reader.readString();
			if (count == 4) return value;
			value.name = reader.readString();
			if (count == 5) return value;
			value.kind = reader.readUint8();
			if (count == 6) return value;
			value.startTime = readDateTimeOffset(reader);
			if (count == 7) return value;
			value.endTime = readDateTimeOffset(reader);
			if (count == 8) return value;
			value.ingestedAt = readDateTimeOffset(reader);
			if (count == 9) return value;
			value.durationNano = reader.readUint64();
			if (count == 10) return value;
			value.statusCode = reader.readString();
			if (count == 11) return value;
			value.statusMessage = reader.readString();
			if (count == 12) return value;
			value.serviceName = reader.readString();
			if (count == 13) return value;
			value.resourceSchemaUrl = reader.readString();
			if (count == 14) return value;
			value.resourceAttributes = readStringRecord(reader);
			if (count == 15) return value;
			value.scopeSchemaUrl = reader.readString();
			if (count == 16) return value;
			value.scopeName = reader.readString();
			if (count == 17) return value;
			value.scopeVersion = reader.readString();
			if (count == 18) return value;
			value.scopeAttributes = readStringRecord(reader);
			if (count == 19) return value;
			value.spanAttributes = readStringRecord(reader);
			if (count == 20) return value;
			value.events = reader.readArray((reader) => SpanEventDto.deserializeCore(reader));
			if (count == 21) return value;
			value.spanCount = reader.readNullableUint64();
			if (count == 22) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (SpanDto | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (SpanDto | null)[] | null {
		return reader.readArray((reader) => SpanDto.deserializeCore(reader));
	}
}
