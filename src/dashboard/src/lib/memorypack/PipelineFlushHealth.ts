// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PipelineModels.cs`'s `PipelineFlushHealth`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `LastFlushAt`/`LastErrorAt` are `DateTimeOffset?` - see
// `$lib/memorypack/date-time-offset.ts`'s header comment. `signal` is a raw MemoryPack
// numeric ordinal (converted to string at `pipeline-api.ts`'s module boundary).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readNullableDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class PipelineFlushHealth {
	signal: number;
	lastFlushAt: Date | null;
	lastBatchSize: bigint | null;
	lastErrorAt: Date | null;
	lastError: string | null;
	consecutiveErrors: bigint;

	constructor() {
		this.signal = 0;
		this.lastFlushAt = null;
		this.lastBatchSize = null;
		this.lastErrorAt = null;
		this.lastError = null;
		this.consecutiveErrors = 0n;
	}

	static serialize(value: PipelineFlushHealth | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PipelineFlushHealth | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeInt32(value.signal);
		writeNullableDateTimeOffset(writer, value.lastFlushAt);
		writer.writeNullableInt64(value.lastBatchSize);
		writeNullableDateTimeOffset(writer, value.lastErrorAt);
		writer.writeString(value.lastError);
		writer.writeInt64(value.consecutiveErrors);
	}

	static serializeArray(value: (PipelineFlushHealth | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (PipelineFlushHealth | null)[] | null): void {
		writer.writeArray(value, (writer, x) => PipelineFlushHealth.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): PipelineFlushHealth | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PipelineFlushHealth | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PipelineFlushHealth();
		if (count == 6) {
			value.signal = reader.readInt32();
			value.lastFlushAt = readNullableDateTimeOffset(reader);
			value.lastBatchSize = reader.readNullableInt64();
			value.lastErrorAt = readNullableDateTimeOffset(reader);
			value.lastError = reader.readString();
			value.consecutiveErrors = reader.readInt64();
		} else if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.signal = reader.readInt32();
			if (count == 1) return value;
			value.lastFlushAt = readNullableDateTimeOffset(reader);
			if (count == 2) return value;
			value.lastBatchSize = reader.readNullableInt64();
			if (count == 3) return value;
			value.lastErrorAt = readNullableDateTimeOffset(reader);
			if (count == 4) return value;
			value.lastError = reader.readString();
			if (count == 5) return value;
			value.consecutiveErrors = reader.readInt64();
			if (count == 6) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (PipelineFlushHealth | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (PipelineFlushHealth | null)[] | null {
		return reader.readArray((reader) => PipelineFlushHealth.deserializeCore(reader));
	}
}
