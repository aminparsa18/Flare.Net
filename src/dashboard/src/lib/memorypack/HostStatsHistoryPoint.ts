// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/HostStatsHistoryPoint.cs`'s
// `HostStatsHistoryPoint` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself because `Timestamp` is a `DateTimeOffset` - see
// `$lib/memorypack/date-time-offset.ts`'s header comment. `GET /api/resources/host/history`
// returns a bare array of these (not wrapped in a response object), so `deserializeArray`
// is this type's actual top-level entry point, same as MemoryPack's own generated classes
// support via `serializeArray`/`deserializeArray`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class HostStatsHistoryPoint {
	timestamp: Date;
	cpuUsagePercent: number;
	memoryUsedPercent: number;
	diskUsedPercent: number;
	networkBytesPerSecond: number;

	constructor() {
		this.timestamp = new Date(0);
		this.cpuUsagePercent = 0;
		this.memoryUsedPercent = 0;
		this.diskUsedPercent = 0;
		this.networkBytesPerSecond = 0;
	}

	static serialize(value: HostStatsHistoryPoint | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: HostStatsHistoryPoint | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writeDateTimeOffset(writer, value.timestamp);
		writer.writeFloat64(value.cpuUsagePercent);
		writer.writeFloat64(value.memoryUsedPercent);
		writer.writeFloat64(value.diskUsedPercent);
		writer.writeFloat64(value.networkBytesPerSecond);
	}

	static serializeArray(value: (HostStatsHistoryPoint | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (HostStatsHistoryPoint | null)[] | null): void {
		writer.writeArray(value, (writer, x) => HostStatsHistoryPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): HostStatsHistoryPoint | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): HostStatsHistoryPoint | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new HostStatsHistoryPoint();
		if (count == 5) {
			value.timestamp = readDateTimeOffset(reader);
			value.cpuUsagePercent = reader.readFloat64();
			value.memoryUsedPercent = reader.readFloat64();
			value.diskUsedPercent = reader.readFloat64();
			value.networkBytesPerSecond = reader.readFloat64();
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.timestamp = readDateTimeOffset(reader);
			if (count == 1) return value;
			value.cpuUsagePercent = reader.readFloat64();
			if (count == 2) return value;
			value.memoryUsedPercent = reader.readFloat64();
			if (count == 3) return value;
			value.diskUsedPercent = reader.readFloat64();
			if (count == 4) return value;
			value.networkBytesPerSecond = reader.readFloat64();
			if (count == 5) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (HostStatsHistoryPoint | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (HostStatsHistoryPoint | null)[] | null {
		return reader.readArray((reader) => HostStatsHistoryPoint.deserializeCore(reader));
	}
}
