// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ResourceGraphDto.cs`'s `ProducerServiceDto`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `LastSeenAt` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class ProducerServiceDto {
	id: string | null;
	serviceName: string | null;
	lastSeenAt: Date;

	constructor() {
		this.id = null;
		this.serviceName = null;
		this.lastSeenAt = new Date(0);
	}

	static serialize(value: ProducerServiceDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ProducerServiceDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeString(value.id);
		writer.writeString(value.serviceName);
		writeDateTimeOffset(writer, value.lastSeenAt);
	}

	static serializeArray(value: (ProducerServiceDto | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (ProducerServiceDto | null)[] | null): void {
		writer.writeArray(value, (writer, x) => ProducerServiceDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ProducerServiceDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ProducerServiceDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ProducerServiceDto();
		if (count == 3) {
			value.id = reader.readString();
			value.serviceName = reader.readString();
			value.lastSeenAt = readDateTimeOffset(reader);
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.id = reader.readString();
			if (count == 1) return value;
			value.serviceName = reader.readString();
			if (count == 2) return value;
			value.lastSeenAt = readDateTimeOffset(reader);
			if (count == 3) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (ProducerServiceDto | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (ProducerServiceDto | null)[] | null {
		return reader.readArray((reader) => ProducerServiceDto.deserializeCore(reader));
	}
}
