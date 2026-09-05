// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/LogEventDto.cs`'s `LogEventDto` field-for-field,
// in declared order. Can't carry `[GenerateTypeScript]` itself because `Timestamp`/
// `ObservedTimestamp`/`IngestedAt` are `DateTimeOffset` - see
// `$lib/memorypack/date-time-offset.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { readStringRecord, writeStringRecord, type StringRecord } from '$lib/memorypack/string-record';

export class LogEventDto {
	eventId: string;
	timestamp: Date;
	observedTimestamp: Date;
	ingestedAt: Date;
	traceId: string | null;
	spanId: string | null;
	traceFlags: number;
	severityText: string | null;
	severityNumber: number;
	serviceName: string | null;
	body: string | null;
	resourceSchemaUrl: string | null;
	resourceAttributes: StringRecord;
	scopeSchemaUrl: string | null;
	scopeName: string | null;
	scopeVersion: string | null;
	scopeAttributes: StringRecord;
	logAttributes: StringRecord;
	eventName: string | null;
	patternId: string | null;
	patternTemplate: string | null;
	spanDurationNano: bigint | null;

	constructor() {
		this.eventId = '00000000-0000-0000-0000-000000000000';
		this.timestamp = new Date(0);
		this.observedTimestamp = new Date(0);
		this.ingestedAt = new Date(0);
		this.traceId = null;
		this.spanId = null;
		this.traceFlags = 0;
		this.severityText = null;
		this.severityNumber = 0;
		this.serviceName = null;
		this.body = null;
		this.resourceSchemaUrl = null;
		this.resourceAttributes = null;
		this.scopeSchemaUrl = null;
		this.scopeName = null;
		this.scopeVersion = null;
		this.scopeAttributes = null;
		this.logAttributes = null;
		this.eventName = null;
		this.patternId = null;
		this.patternTemplate = null;
		this.spanDurationNano = null;
	}

	static serialize(value: LogEventDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: LogEventDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(22);
		writer.writeGuid(value.eventId);
		writeDateTimeOffset(writer, value.timestamp);
		writeDateTimeOffset(writer, value.observedTimestamp);
		writeDateTimeOffset(writer, value.ingestedAt);
		writer.writeString(value.traceId);
		writer.writeString(value.spanId);
		writer.writeUint8(value.traceFlags);
		writer.writeString(value.severityText);
		writer.writeUint8(value.severityNumber);
		writer.writeString(value.serviceName);
		writer.writeString(value.body);
		writer.writeString(value.resourceSchemaUrl);
		writeStringRecord(writer, value.resourceAttributes);
		writer.writeString(value.scopeSchemaUrl);
		writer.writeString(value.scopeName);
		writer.writeString(value.scopeVersion);
		writeStringRecord(writer, value.scopeAttributes);
		writeStringRecord(writer, value.logAttributes);
		writer.writeString(value.eventName);
		writer.writeString(value.patternId);
		writer.writeString(value.patternTemplate);
		writer.writeNullableUint64(value.spanDurationNano);
	}

	static serializeArray(value: (LogEventDto | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (LogEventDto | null)[] | null): void {
		writer.writeArray(value, (writer, x) => LogEventDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): LogEventDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): LogEventDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new LogEventDto();
		if (count == 22) {
			value.eventId = reader.readGuid();
			value.timestamp = readDateTimeOffset(reader);
			value.observedTimestamp = readDateTimeOffset(reader);
			value.ingestedAt = readDateTimeOffset(reader);
			value.traceId = reader.readString();
			value.spanId = reader.readString();
			value.traceFlags = reader.readUint8();
			value.severityText = reader.readString();
			value.severityNumber = reader.readUint8();
			value.serviceName = reader.readString();
			value.body = reader.readString();
			value.resourceSchemaUrl = reader.readString();
			value.resourceAttributes = readStringRecord(reader);
			value.scopeSchemaUrl = reader.readString();
			value.scopeName = reader.readString();
			value.scopeVersion = reader.readString();
			value.scopeAttributes = readStringRecord(reader);
			value.logAttributes = readStringRecord(reader);
			value.eventName = reader.readString();
			value.patternId = reader.readString();
			value.patternTemplate = reader.readString();
			value.spanDurationNano = reader.readNullableUint64();
		} else if (count > 22) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.eventId = reader.readGuid();
			if (count == 1) return value;
			value.timestamp = readDateTimeOffset(reader);
			if (count == 2) return value;
			value.observedTimestamp = readDateTimeOffset(reader);
			if (count == 3) return value;
			value.ingestedAt = readDateTimeOffset(reader);
			if (count == 4) return value;
			value.traceId = reader.readString();
			if (count == 5) return value;
			value.spanId = reader.readString();
			if (count == 6) return value;
			value.traceFlags = reader.readUint8();
			if (count == 7) return value;
			value.severityText = reader.readString();
			if (count == 8) return value;
			value.severityNumber = reader.readUint8();
			if (count == 9) return value;
			value.serviceName = reader.readString();
			if (count == 10) return value;
			value.body = reader.readString();
			if (count == 11) return value;
			value.resourceSchemaUrl = reader.readString();
			if (count == 12) return value;
			value.resourceAttributes = readStringRecord(reader);
			if (count == 13) return value;
			value.scopeSchemaUrl = reader.readString();
			if (count == 14) return value;
			value.scopeName = reader.readString();
			if (count == 15) return value;
			value.scopeVersion = reader.readString();
			if (count == 16) return value;
			value.scopeAttributes = readStringRecord(reader);
			if (count == 17) return value;
			value.logAttributes = readStringRecord(reader);
			if (count == 18) return value;
			value.eventName = reader.readString();
			if (count == 19) return value;
			value.patternId = reader.readString();
			if (count == 20) return value;
			value.patternTemplate = reader.readString();
			if (count == 21) return value;
			value.spanDurationNano = reader.readNullableUint64();
			if (count == 22) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (LogEventDto | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (LogEventDto | null)[] | null {
		return reader.readArray((reader) => LogEventDto.deserializeCore(reader));
	}
}
