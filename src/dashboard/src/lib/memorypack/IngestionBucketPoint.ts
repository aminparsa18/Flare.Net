// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/IngestionModels.cs`'s `IngestionBucketPoint`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `BucketStart` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment. `signal`/`protocol` are raw MemoryPack numeric ordinals here (not converted to
// string at this layer, unlike the settings DTOs' `defaultRole`) - `ingestion-api.ts`
// converts them at its own module boundary via `$lib/memorypack/enums.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class IngestionBucketPoint {
	bucketStart: Date;
	signal: number;
	protocol: number;
	requests: bigint;
	records: bigint;
	bytes: bigint;
	rejected: bigint;

	constructor() {
		this.bucketStart = new Date(0);
		this.signal = 0;
		this.protocol = 0;
		this.requests = 0n;
		this.records = 0n;
		this.bytes = 0n;
		this.rejected = 0n;
	}

	static serialize(value: IngestionBucketPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: IngestionBucketPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(7);
		writeDateTimeOffset(writer, value.bucketStart);
		writer.writeInt32(value.signal);
		writer.writeInt32(value.protocol);
		writer.writeInt64(value.requests);
		writer.writeInt64(value.records);
		writer.writeInt64(value.bytes);
		writer.writeInt64(value.rejected);
	}

	static serializeArray(value: (IngestionBucketPoint | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (IngestionBucketPoint | null)[] | null): void {
		writer.writeArray(value, (writer, x) => IngestionBucketPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): IngestionBucketPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): IngestionBucketPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new IngestionBucketPoint();
		if (count == 7) {
			value.bucketStart = readDateTimeOffset(reader);
			value.signal = reader.readInt32();
			value.protocol = reader.readInt32();
			value.requests = reader.readInt64();
			value.records = reader.readInt64();
			value.bytes = reader.readInt64();
			value.rejected = reader.readInt64();
		} else if (count > 7) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.bucketStart = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.signal = reader.readInt32();
			if (count == 2) return value;
			value.protocol = reader.readInt32();
			if (count == 3) return value;
			value.requests = reader.readInt64();
			if (count == 4) return value;
			value.records = reader.readInt64();
			if (count == 5) return value;
			value.bytes = reader.readInt64();
			if (count == 6) return value;
			value.rejected = reader.readInt64();
			if (count == 7) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (IngestionBucketPoint | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (IngestionBucketPoint | null)[] | null {
		return reader.readArray((reader) => IngestionBucketPoint.deserializeCore(reader));
	}
}
