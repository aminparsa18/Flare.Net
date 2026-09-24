// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/IngestApiKeyModels.cs`'s `IngestApiKeyDto`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `CreatedAt`/`RevokedAt` are `DateTimeOffset`/`DateTimeOffset?` - see
// `$lib/memorypack/date-time-offset.ts`'s header comment. The limit/usage members
// (ADR-0051) are `long`/`long?` on the wire, read as `bigint` like the generated classes do.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, readNullableDateTimeOffset, writeDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class IngestApiKeyDto {
	id: string;
	name: string;
	createdAt: Date;
	revokedAt: Date | null;
	isActive: boolean;
	limitsEnabled: boolean;
	maxEventsPerMinute: bigint | null;
	maxBytesPerMinute: bigint | null;
	maxEventsPerDay: bigint | null;
	maxBytesPerDay: bigint | null;
	eventsThisMinute: bigint;
	bytesThisMinute: bigint;
	eventsToday: bigint;
	bytesToday: bigint;

	constructor() {
		this.id = '00000000-0000-0000-0000-000000000000';
		this.name = '';
		this.createdAt = new Date(0);
		this.revokedAt = null;
		this.isActive = false;
		this.limitsEnabled = false;
		this.maxEventsPerMinute = null;
		this.maxBytesPerMinute = null;
		this.maxEventsPerDay = null;
		this.maxBytesPerDay = null;
		this.eventsThisMinute = 0n;
		this.bytesThisMinute = 0n;
		this.eventsToday = 0n;
		this.bytesToday = 0n;
	}

	static serialize(value: IngestApiKeyDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: IngestApiKeyDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(14);
		writer.writeGuid(value.id);
		writer.writeString(value.name);
		writeDateTimeOffset(writer, value.createdAt);
		writeNullableDateTimeOffset(writer, value.revokedAt);
		writer.writeBoolean(value.isActive);
		writer.writeBoolean(value.limitsEnabled);
		writer.writeNullableInt64(value.maxEventsPerMinute);
		writer.writeNullableInt64(value.maxBytesPerMinute);
		writer.writeNullableInt64(value.maxEventsPerDay);
		writer.writeNullableInt64(value.maxBytesPerDay);
		writer.writeInt64(value.eventsThisMinute);
		writer.writeInt64(value.bytesThisMinute);
		writer.writeInt64(value.eventsToday);
		writer.writeInt64(value.bytesToday);
	}

	static serializeArray(value: (IngestApiKeyDto | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (IngestApiKeyDto | null)[] | null): void {
		writer.writeArray(value, (writer, x) => IngestApiKeyDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): IngestApiKeyDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): IngestApiKeyDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new IngestApiKeyDto();
		if (count == 14) {
			value.id = reader.readGuid();
			value.name = reader.readString() ?? '';
			value.createdAt = readDateTimeOffset(reader);
			value.revokedAt = readNullableDateTimeOffset(reader);
			value.isActive = reader.readBoolean();
			value.limitsEnabled = reader.readBoolean();
			value.maxEventsPerMinute = reader.readNullableInt64();
			value.maxBytesPerMinute = reader.readNullableInt64();
			value.maxEventsPerDay = reader.readNullableInt64();
			value.maxBytesPerDay = reader.readNullableInt64();
			value.eventsThisMinute = reader.readInt64();
			value.bytesThisMinute = reader.readInt64();
			value.eventsToday = reader.readInt64();
			value.bytesToday = reader.readInt64();
		} else if (count > 14) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.id = reader.readGuid();
			if (count == 1) return value;
			value.name = reader.readString() ?? '';
			if (count == 2) return value;
			value.createdAt = readDateTimeOffset(reader);
			if (count == 3) return value;
			value.revokedAt = readNullableDateTimeOffset(reader);
			if (count == 4) return value;
			value.isActive = reader.readBoolean();
			if (count == 5) return value;
			value.limitsEnabled = reader.readBoolean();
			if (count == 6) return value;
			value.maxEventsPerMinute = reader.readNullableInt64();
			if (count == 7) return value;
			value.maxBytesPerMinute = reader.readNullableInt64();
			if (count == 8) return value;
			value.maxEventsPerDay = reader.readNullableInt64();
			if (count == 9) return value;
			value.maxBytesPerDay = reader.readNullableInt64();
			if (count == 10) return value;
			value.eventsThisMinute = reader.readInt64();
			if (count == 11) return value;
			value.bytesThisMinute = reader.readInt64();
			if (count == 12) return value;
			value.eventsToday = reader.readInt64();
			if (count == 13) return value;
			value.bytesToday = reader.readInt64();
			if (count == 14) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (IngestApiKeyDto | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (IngestApiKeyDto | null)[] | null {
		return reader.readArray((reader) => IngestApiKeyDto.deserializeCore(reader));
	}
}
