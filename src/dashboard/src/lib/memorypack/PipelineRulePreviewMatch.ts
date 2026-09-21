// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PipelineRuleModels.cs`'s `PipelineRulePreviewMatch`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `Timestamp` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { readStringRecord, writeStringRecord, type StringRecord } from '$lib/memorypack/string-record';

export class PipelineRulePreviewMatch {
	eventId: string;
	timestamp: Date;
	serviceName: string | null;
	beforeBody: string | null;
	afterBody: string | null;
	beforeAttributes: StringRecord;
	afterAttributes: StringRecord;
	changed: boolean;

	constructor() {
		this.eventId = '00000000-0000-0000-0000-000000000000';
		this.timestamp = new Date(0);
		this.serviceName = null;
		this.beforeBody = null;
		this.afterBody = null;
		this.beforeAttributes = null;
		this.afterAttributes = null;
		this.changed = false;
	}

	static serialize(value: PipelineRulePreviewMatch | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PipelineRulePreviewMatch | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(8);
		writer.writeGuid(value.eventId);
		writeDateTimeOffset(writer, value.timestamp);
		writer.writeString(value.serviceName);
		writer.writeString(value.beforeBody);
		writer.writeString(value.afterBody);
		writeStringRecord(writer, value.beforeAttributes);
		writeStringRecord(writer, value.afterAttributes);
		writer.writeBoolean(value.changed);
	}

	static serializeArray(value: (PipelineRulePreviewMatch | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (PipelineRulePreviewMatch | null)[] | null): void {
		writer.writeArray(value, (writer, x) => PipelineRulePreviewMatch.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): PipelineRulePreviewMatch | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PipelineRulePreviewMatch | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PipelineRulePreviewMatch();
		if (count == 8) {
			value.eventId = reader.readGuid();
			value.timestamp = readDateTimeOffset(reader);
			value.serviceName = reader.readString();
			value.beforeBody = reader.readString();
			value.afterBody = reader.readString();
			value.beforeAttributes = readStringRecord(reader);
			value.afterAttributes = readStringRecord(reader);
			value.changed = reader.readBoolean();
		} else if (count > 8) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.eventId = reader.readGuid();
			if (count == 1) return value;
			value.timestamp = readDateTimeOffset(reader);
			if (count == 2) return value;
			value.serviceName = reader.readString();
			if (count == 3) return value;
			value.beforeBody = reader.readString();
			if (count == 4) return value;
			value.afterBody = reader.readString();
			if (count == 5) return value;
			value.beforeAttributes = readStringRecord(reader);
			if (count == 6) return value;
			value.afterAttributes = readStringRecord(reader);
			if (count == 7) return value;
			value.changed = reader.readBoolean();
			if (count == 8) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (PipelineRulePreviewMatch | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (PipelineRulePreviewMatch | null)[] | null {
		return reader.readArray((reader) => PipelineRulePreviewMatch.deserializeCore(reader));
	}
}
