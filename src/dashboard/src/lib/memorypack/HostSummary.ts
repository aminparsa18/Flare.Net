// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/HostInventoryModels.cs`'s `HostSummary`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `LastSeen` is a `DateTimeOffset` - see `$lib/memorypack/date-time-offset.ts`'s header
// comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class HostSummary {
	hostName: string;
	osType: string | null;
	cpuPercent: number | null;
	memoryPercent: number | null;
	diskPercent: number | null;
	loadAverage15m: number | null;
	lastSeen: Date;

	constructor() {
		this.hostName = '';
		this.osType = null;
		this.cpuPercent = null;
		this.memoryPercent = null;
		this.diskPercent = null;
		this.loadAverage15m = null;
		this.lastSeen = new Date(0);
	}

	static serialize(value: HostSummary | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: HostSummary | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(7);
		writer.writeString(value.hostName);
		writer.writeString(value.osType);
		writer.writeNullableFloat64(value.cpuPercent);
		writer.writeNullableFloat64(value.memoryPercent);
		writer.writeNullableFloat64(value.diskPercent);
		writer.writeNullableFloat64(value.loadAverage15m);
		writeDateTimeOffset(writer, value.lastSeen);
	}

	static deserialize(buffer: ArrayBuffer): HostSummary | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): HostSummary | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new HostSummary();
		if (count == 7) {
			value.hostName = reader.readString() ?? '';
			value.osType = reader.readString();
			value.cpuPercent = reader.readNullableFloat64();
			value.memoryPercent = reader.readNullableFloat64();
			value.diskPercent = reader.readNullableFloat64();
			value.loadAverage15m = reader.readNullableFloat64();
			value.lastSeen = readDateTimeOffset(reader);
		} else if (count > 7) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.hostName = reader.readString() ?? '';
			if (count == 1) return value;
			value.osType = reader.readString();
			if (count == 2) return value;
			value.cpuPercent = reader.readNullableFloat64();
			if (count == 3) return value;
			value.memoryPercent = reader.readNullableFloat64();
			if (count == 4) return value;
			value.diskPercent = reader.readNullableFloat64();
			if (count == 5) return value;
			value.loadAverage15m = reader.readNullableFloat64();
			if (count == 6) return value;
			value.lastSeen = readDateTimeOffset(reader);
		}
		return value;
	}
}
