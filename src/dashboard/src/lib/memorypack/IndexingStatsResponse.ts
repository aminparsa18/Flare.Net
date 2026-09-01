// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/IndexingModels.cs`'s `IndexingStatsResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself: it has its
// own `DateTimeOffset GeneratedAt` member and nests `IReadOnlyList<StorageGrowthPoint>`
// (`StorageGrowthPoint` itself has a `DateTimeOffset Day` member). `TableStorageInfo`/
// `SkipIndexInfo`/`DiskUsageInfo`/`QueryPerformanceInfo` have neither problem, so they're
// real generated classes, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { TableStorageInfo } from '$lib/generated/memorypack/TableStorageInfo.js';
import { SkipIndexInfo } from '$lib/generated/memorypack/SkipIndexInfo.js';
import { DiskUsageInfo } from '$lib/generated/memorypack/DiskUsageInfo.js';
import { QueryPerformanceInfo } from '$lib/generated/memorypack/QueryPerformanceInfo.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';
import { StorageGrowthPoint } from '$lib/memorypack/StorageGrowthPoint';

export class IndexingStatsResponse {
	generatedAt: Date;
	tables: (TableStorageInfo | null)[] | null;
	skipIndexes: (SkipIndexInfo | null)[] | null;
	growth: (StorageGrowthPoint | null)[] | null;
	growthAvailable: boolean;
	diskUsage: DiskUsageInfo | null;
	queryPerformance: QueryPerformanceInfo | null;

	constructor() {
		this.generatedAt = new Date(0);
		this.tables = null;
		this.skipIndexes = null;
		this.growth = null;
		this.growthAvailable = false;
		this.diskUsage = null;
		this.queryPerformance = null;
	}

	static serialize(value: IndexingStatsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: IndexingStatsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(7);
		writeDateTimeOffset(writer, value.generatedAt);
		writer.writeArray(value.tables, (writer, x) => TableStorageInfo.serializeCore(writer, x));
		writer.writeArray(value.skipIndexes, (writer, x) => SkipIndexInfo.serializeCore(writer, x));
		writer.writeArray(value.growth, (writer, x) => StorageGrowthPoint.serializeCore(writer, x));
		writer.writeBoolean(value.growthAvailable);
		DiskUsageInfo.serializeCore(writer, value.diskUsage);
		QueryPerformanceInfo.serializeCore(writer, value.queryPerformance);
	}

	static deserialize(buffer: ArrayBuffer): IndexingStatsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): IndexingStatsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new IndexingStatsResponse();
		if (count == 7) {
			value.generatedAt = readDateTimeOffset(reader);
			value.tables = reader.readArray((reader) => TableStorageInfo.deserializeCore(reader));
			value.skipIndexes = reader.readArray((reader) => SkipIndexInfo.deserializeCore(reader));
			value.growth = reader.readArray((reader) => StorageGrowthPoint.deserializeCore(reader));
			value.growthAvailable = reader.readBoolean();
			value.diskUsage = DiskUsageInfo.deserializeCore(reader);
			value.queryPerformance = QueryPerformanceInfo.deserializeCore(reader);
		} else if (count > 7) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.generatedAt = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.tables = reader.readArray((reader) => TableStorageInfo.deserializeCore(reader));
			if (count == 2) return value;
			value.skipIndexes = reader.readArray((reader) => SkipIndexInfo.deserializeCore(reader));
			if (count == 3) return value;
			value.growth = reader.readArray((reader) => StorageGrowthPoint.deserializeCore(reader));
			if (count == 4) return value;
			value.growthAvailable = reader.readBoolean();
			if (count == 5) return value;
			value.diskUsage = DiskUsageInfo.deserializeCore(reader);
			if (count == 6) return value;
			value.queryPerformance = QueryPerformanceInfo.deserializeCore(reader);
			if (count == 7) return value;
		}
		return value;
	}
}
