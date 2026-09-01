// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `AlertHistoryEntry`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `FiredAt` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class AlertHistoryEntry {
	eventId: string;
	ruleId: string;
	ruleName: string | null;
	firedAt: Date;
	observedCount: bigint;
	thresholdCount: bigint;
	windowSeconds: number;
	notificationStatus: string | null;
	notificationStatusCode: number;
	notificationError: string | null;

	constructor() {
		this.eventId = '00000000-0000-0000-0000-000000000000';
		this.ruleId = '00000000-0000-0000-0000-000000000000';
		this.ruleName = null;
		this.firedAt = new Date(0);
		this.observedCount = 0n;
		this.thresholdCount = 0n;
		this.windowSeconds = 0;
		this.notificationStatus = null;
		this.notificationStatusCode = 0;
		this.notificationError = null;
	}

	static serialize(value: AlertHistoryEntry | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: AlertHistoryEntry | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(10);
		writer.writeGuid(value.eventId);
		writer.writeGuid(value.ruleId);
		writer.writeString(value.ruleName);
		writeDateTimeOffset(writer, value.firedAt);
		writer.writeUint64(value.observedCount);
		writer.writeUint64(value.thresholdCount);
		writer.writeInt32(value.windowSeconds);
		writer.writeString(value.notificationStatus);
		writer.writeInt32(value.notificationStatusCode);
		writer.writeString(value.notificationError);
	}

	static serializeArray(value: (AlertHistoryEntry | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (AlertHistoryEntry | null)[] | null): void {
		writer.writeArray(value, (writer, x) => AlertHistoryEntry.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): AlertHistoryEntry | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): AlertHistoryEntry | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new AlertHistoryEntry();
		if (count == 10) {
			value.eventId = reader.readGuid();
			value.ruleId = reader.readGuid();
			value.ruleName = reader.readString();
			value.firedAt = readDateTimeOffset(reader);
			value.observedCount = reader.readUint64();
			value.thresholdCount = reader.readUint64();
			value.windowSeconds = reader.readInt32();
			value.notificationStatus = reader.readString();
			value.notificationStatusCode = reader.readInt32();
			value.notificationError = reader.readString();
		} else if (count > 10) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.eventId = reader.readGuid();
			if (count == 1) return value;
			value.ruleId = reader.readGuid();
			if (count == 2) return value;
			value.ruleName = reader.readString();
			if (count == 3) return value;
			value.firedAt = readDateTimeOffset(reader);
			if (count == 4) return value;
			value.observedCount = reader.readUint64();
			if (count == 5) return value;
			value.thresholdCount = reader.readUint64();
			if (count == 6) return value;
			value.windowSeconds = reader.readInt32();
			if (count == 7) return value;
			value.notificationStatus = reader.readString();
			if (count == 8) return value;
			value.notificationStatusCode = reader.readInt32();
			if (count == 9) return value;
			value.notificationError = reader.readString();
			if (count == 10) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (AlertHistoryEntry | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (AlertHistoryEntry | null)[] | null {
		return reader.readArray((reader) => AlertHistoryEntry.deserializeCore(reader));
	}
}
