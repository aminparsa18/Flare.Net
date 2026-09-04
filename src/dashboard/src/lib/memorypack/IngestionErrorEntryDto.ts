// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/IngestionModels.cs`'s `IngestionErrorEntryDto`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `Timestamp` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment. `Signal`/`Protocol` are plain C# `string`s here (not the `IngestionSignal`/
// `IngestionProtocol` enums), so they round-trip with no conversion.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class IngestionErrorEntryDto {
	timestamp: Date;
	signal: string | null;
	protocol: string | null;
	reason: string | null;

	constructor() {
		this.timestamp = new Date(0);
		this.signal = null;
		this.protocol = null;
		this.reason = null;
	}

	static serialize(value: IngestionErrorEntryDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: IngestionErrorEntryDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writeDateTimeOffset(writer, value.timestamp);
		writer.writeString(value.signal);
		writer.writeString(value.protocol);
		writer.writeString(value.reason);
	}

	static serializeArray(value: (IngestionErrorEntryDto | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (IngestionErrorEntryDto | null)[] | null): void {
		writer.writeArray(value, (writer, x) => IngestionErrorEntryDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): IngestionErrorEntryDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): IngestionErrorEntryDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new IngestionErrorEntryDto();
		if (count == 4) {
			value.timestamp = readDateTimeOffset(reader);
			value.signal = reader.readString();
			value.protocol = reader.readString();
			value.reason = reader.readString();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.timestamp = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.signal = reader.readString();
			if (count == 2) return value;
			value.protocol = reader.readString();
			if (count == 3) return value;
			value.reason = reader.readString();
			if (count == 4) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (IngestionErrorEntryDto | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (IngestionErrorEntryDto | null)[] | null {
		return reader.readArray((reader) => IngestionErrorEntryDto.deserializeCore(reader));
	}
}
