// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/IndexingModels.cs`'s `StorageGrowthPoint`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `Day` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class StorageGrowthPoint {
	day: Date;
	tableName: string | null;
	bytes: bigint;
	rows: bigint;

	constructor() {
		this.day = new Date(0);
		this.tableName = null;
		this.bytes = 0n;
		this.rows = 0n;
	}

	static serialize(value: StorageGrowthPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: StorageGrowthPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writeDateTimeOffset(writer, value.day);
		writer.writeString(value.tableName);
		writer.writeInt64(value.bytes);
		writer.writeInt64(value.rows);
	}

	static serializeArray(value: (StorageGrowthPoint | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (StorageGrowthPoint | null)[] | null): void {
		writer.writeArray(value, (writer, x) => StorageGrowthPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): StorageGrowthPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): StorageGrowthPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new StorageGrowthPoint();
		if (count == 4) {
			value.day = readDateTimeOffset(reader);
			value.tableName = reader.readString();
			value.bytes = reader.readInt64();
			value.rows = reader.readInt64();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.day = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.tableName = reader.readString();
			if (count == 2) return value;
			value.bytes = reader.readInt64();
			if (count == 3) return value;
			value.rows = reader.readInt64();
			if (count == 4) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (StorageGrowthPoint | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (StorageGrowthPoint | null)[] | null {
		return reader.readArray((reader) => StorageGrowthPoint.deserializeCore(reader));
	}
}
