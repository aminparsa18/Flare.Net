// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/IngestionModels.cs`'s `IngestionStatsResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because it
// has its own `DateTimeOffset GeneratedAt` member and nests `IngestionBucketPoint`/
// `IngestionErrorEntryDto` (both also hand-written) - see `$lib/memorypack/date-time-offset.ts`'s
// header comment. `Totals` (`IngestionStatsTotals`) has no DateTimeOffset/JsonElement member
// so it's a real generated class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { IngestionStatsTotals } from '$lib/generated/memorypack/IngestionStatsTotals.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { IngestionBucketPoint } from '$lib/memorypack/IngestionBucketPoint';
import { IngestionErrorEntryDto } from '$lib/memorypack/IngestionErrorEntryDto';

export class IngestionStatsResponse {
	generatedAt: Date;
	minutes: number;
	buckets: (IngestionBucketPoint | null)[] | null;
	totals: IngestionStatsTotals | null;
	recentErrors: (IngestionErrorEntryDto | null)[] | null;

	constructor() {
		this.generatedAt = new Date(0);
		this.minutes = 0;
		this.buckets = null;
		this.totals = null;
		this.recentErrors = null;
	}

	static serialize(value: IngestionStatsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: IngestionStatsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writeDateTimeOffset(writer, value.generatedAt);
		writer.writeInt32(value.minutes);
		writer.writeArray(value.buckets, (writer, x) => IngestionBucketPoint.serializeCore(writer, x));
		IngestionStatsTotals.serializeCore(writer, value.totals);
		writer.writeArray(value.recentErrors, (writer, x) => IngestionErrorEntryDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): IngestionStatsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): IngestionStatsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new IngestionStatsResponse();
		if (count == 5) {
			value.generatedAt = readDateTimeOffset(reader);
			value.minutes = reader.readInt32();
			value.buckets = reader.readArray((reader) => IngestionBucketPoint.deserializeCore(reader));
			value.totals = IngestionStatsTotals.deserializeCore(reader);
			value.recentErrors = reader.readArray((reader) => IngestionErrorEntryDto.deserializeCore(reader));
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.generatedAt = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.minutes = reader.readInt32();
			if (count == 2) return value;
			value.buckets = reader.readArray((reader) => IngestionBucketPoint.deserializeCore(reader));
			if (count == 3) return value;
			value.totals = IngestionStatsTotals.deserializeCore(reader);
			if (count == 4) return value;
			value.recentErrors = reader.readArray((reader) => IngestionErrorEntryDto.deserializeCore(reader));
		}
		return value;
	}
}
