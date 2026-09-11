// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/AlertModels.cs`'s `AlertHistoryEntry`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `FiredAt` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment. `conditionKind`/`observedValue`/`thresholdValue`/`channelResults` were appended
// after every pre-existing field, same versioning reasoning as `AlertRule.ts`.
// `channelResults`' element type, `AlertChannelResult`, has no such problem (flat fields
// only) so it's a real generated class, reused here directly - same shape `threshold`
// (`AlertThreshold`) already has on `AlertRule.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { AlertChannelResult } from '$lib/generated/memorypack/AlertChannelResult.js';
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
	conditionKind: number;
	observedValue: number | null;
	thresholdValue: number | null;
	channelResults: (AlertChannelResult | null)[] | null;

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
		this.conditionKind = 0;
		this.observedValue = null;
		this.thresholdValue = null;
		this.channelResults = null;
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

		writer.writeObjectHeader(14);
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
		writer.writeInt32(value.conditionKind);
		writer.writeNullableFloat64(value.observedValue);
		writer.writeNullableFloat64(value.thresholdValue);
		writer.writeArray(value.channelResults, (writer, x) => AlertChannelResult.serializeCore(writer, x));
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
		if (count == 14) {
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
			value.conditionKind = reader.readInt32();
			value.observedValue = reader.readNullableFloat64();
			value.thresholdValue = reader.readNullableFloat64();
			value.channelResults = reader.readArray((reader) => AlertChannelResult.deserializeCore(reader));
		} else if (count > 14) {
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
			value.conditionKind = reader.readInt32();
			if (count == 11) return value;
			value.observedValue = reader.readNullableFloat64();
			if (count == 12) return value;
			value.thresholdValue = reader.readNullableFloat64();
			if (count == 13) return value;
			value.channelResults = reader.readArray((reader) => AlertChannelResult.deserializeCore(reader));
			if (count == 14) return value;
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
