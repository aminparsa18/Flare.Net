// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/HostStatsSnapshot.cs`'s `HostStatsSnapshot`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself: its
// `PerCoreUsagePercent` member is an `IReadOnlyList<double>` (see
// `PipelineServiceBreakdown.ts`'s header comment for why `IReadOnlyList<T>` alone blocks
// `[GenerateTypeScript]`) and it also has its own `DateTimeOffset? UpdatedAt` - see
// `$lib/memorypack/date-time-offset.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readNullableDateTimeOffset, writeNullableDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class HostStatsSnapshot {
	available: boolean;
	unavailableReason: string | null;
	cpuUsagePercent: number;
	cpuCoreCount: number;
	loadAverage1m: number;
	perCoreUsagePercent: number[] | null;
	memoryTotalBytes: bigint;
	memoryUsedBytes: bigint;
	memoryAvailableBytes: bigint;
	swapTotalBytes: bigint;
	swapUsedBytes: bigint;
	diskTotalBytes: bigint;
	diskUsedBytes: bigint;
	diskAvailableBytes: bigint;
	diskGrowthBytesPerDay: number | null;
	diskGrowthWindowHours: number;
	diskReadBytesPerSecond: number;
	diskWriteBytesPerSecond: number;
	networkBytesPerSecond: number;
	networkRxBytesPerSecond: number;
	networkTxBytesPerSecond: number;
	networkPacketsPerSecond: number;
	uptimeSeconds: number;
	updatedAt: Date | null;

	constructor() {
		this.available = false;
		this.unavailableReason = null;
		this.cpuUsagePercent = 0;
		this.cpuCoreCount = 0;
		this.loadAverage1m = 0;
		this.perCoreUsagePercent = null;
		this.memoryTotalBytes = 0n;
		this.memoryUsedBytes = 0n;
		this.memoryAvailableBytes = 0n;
		this.swapTotalBytes = 0n;
		this.swapUsedBytes = 0n;
		this.diskTotalBytes = 0n;
		this.diskUsedBytes = 0n;
		this.diskAvailableBytes = 0n;
		this.diskGrowthBytesPerDay = null;
		this.diskGrowthWindowHours = 0;
		this.diskReadBytesPerSecond = 0;
		this.diskWriteBytesPerSecond = 0;
		this.networkBytesPerSecond = 0;
		this.networkRxBytesPerSecond = 0;
		this.networkTxBytesPerSecond = 0;
		this.networkPacketsPerSecond = 0;
		this.uptimeSeconds = 0;
		this.updatedAt = null;
	}

	static serialize(value: HostStatsSnapshot | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: HostStatsSnapshot | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(24);
		writer.writeBoolean(value.available);
		writer.writeString(value.unavailableReason);
		writer.writeFloat64(value.cpuUsagePercent);
		writer.writeInt32(value.cpuCoreCount);
		writer.writeFloat64(value.loadAverage1m);
		writer.writeArray(value.perCoreUsagePercent, (writer, x) => writer.writeFloat64(x));
		writer.writeInt64(value.memoryTotalBytes);
		writer.writeInt64(value.memoryUsedBytes);
		writer.writeInt64(value.memoryAvailableBytes);
		writer.writeInt64(value.swapTotalBytes);
		writer.writeInt64(value.swapUsedBytes);
		writer.writeInt64(value.diskTotalBytes);
		writer.writeInt64(value.diskUsedBytes);
		writer.writeInt64(value.diskAvailableBytes);
		writer.writeNullableFloat64(value.diskGrowthBytesPerDay);
		writer.writeFloat64(value.diskGrowthWindowHours);
		writer.writeFloat64(value.diskReadBytesPerSecond);
		writer.writeFloat64(value.diskWriteBytesPerSecond);
		writer.writeFloat64(value.networkBytesPerSecond);
		writer.writeFloat64(value.networkRxBytesPerSecond);
		writer.writeFloat64(value.networkTxBytesPerSecond);
		writer.writeFloat64(value.networkPacketsPerSecond);
		writer.writeFloat64(value.uptimeSeconds);
		writeNullableDateTimeOffset(writer, value.updatedAt);
	}

	static deserialize(buffer: ArrayBuffer): HostStatsSnapshot | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): HostStatsSnapshot | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new HostStatsSnapshot();
		if (count == 24) {
			value.available = reader.readBoolean();
			value.unavailableReason = reader.readString();
			value.cpuUsagePercent = reader.readFloat64();
			value.cpuCoreCount = reader.readInt32();
			value.loadAverage1m = reader.readFloat64();
			value.perCoreUsagePercent = reader.readArray((reader) => reader.readFloat64());
			value.memoryTotalBytes = reader.readInt64();
			value.memoryUsedBytes = reader.readInt64();
			value.memoryAvailableBytes = reader.readInt64();
			value.swapTotalBytes = reader.readInt64();
			value.swapUsedBytes = reader.readInt64();
			value.diskTotalBytes = reader.readInt64();
			value.diskUsedBytes = reader.readInt64();
			value.diskAvailableBytes = reader.readInt64();
			value.diskGrowthBytesPerDay = reader.readNullableFloat64();
			value.diskGrowthWindowHours = reader.readFloat64();
			value.diskReadBytesPerSecond = reader.readFloat64();
			value.diskWriteBytesPerSecond = reader.readFloat64();
			value.networkBytesPerSecond = reader.readFloat64();
			value.networkRxBytesPerSecond = reader.readFloat64();
			value.networkTxBytesPerSecond = reader.readFloat64();
			value.networkPacketsPerSecond = reader.readFloat64();
			value.uptimeSeconds = reader.readFloat64();
			value.updatedAt = readNullableDateTimeOffset(reader);
		} else if (count > 24) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.available = reader.readBoolean();
			if (count == 1) return value;
			value.unavailableReason = reader.readString();
			if (count == 2) return value;
			value.cpuUsagePercent = reader.readFloat64();
			if (count == 3) return value;
			value.cpuCoreCount = reader.readInt32();
			if (count == 4) return value;
			value.loadAverage1m = reader.readFloat64();
			if (count == 5) return value;
			value.perCoreUsagePercent = reader.readArray((reader) => reader.readFloat64());
			if (count == 6) return value;
			value.memoryTotalBytes = reader.readInt64();
			if (count == 7) return value;
			value.memoryUsedBytes = reader.readInt64();
			if (count == 8) return value;
			value.memoryAvailableBytes = reader.readInt64();
			if (count == 9) return value;
			value.swapTotalBytes = reader.readInt64();
			if (count == 10) return value;
			value.swapUsedBytes = reader.readInt64();
			if (count == 11) return value;
			value.diskTotalBytes = reader.readInt64();
			if (count == 12) return value;
			value.diskUsedBytes = reader.readInt64();
			if (count == 13) return value;
			value.diskAvailableBytes = reader.readInt64();
			if (count == 14) return value;
			value.diskGrowthBytesPerDay = reader.readNullableFloat64();
			if (count == 15) return value;
			value.diskGrowthWindowHours = reader.readFloat64();
			if (count == 16) return value;
			value.diskReadBytesPerSecond = reader.readFloat64();
			if (count == 17) return value;
			value.diskWriteBytesPerSecond = reader.readFloat64();
			if (count == 18) return value;
			value.networkBytesPerSecond = reader.readFloat64();
			if (count == 19) return value;
			value.networkRxBytesPerSecond = reader.readFloat64();
			if (count == 20) return value;
			value.networkTxBytesPerSecond = reader.readFloat64();
			if (count == 21) return value;
			value.networkPacketsPerSecond = reader.readFloat64();
			if (count == 22) return value;
			value.uptimeSeconds = reader.readFloat64();
			if (count == 23) return value;
			value.updatedAt = readNullableDateTimeOffset(reader);
			if (count == 24) return value;
		}
		return value;
	}
}
