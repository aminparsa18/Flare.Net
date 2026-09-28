// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesInventoryModels.cs`'s `KubernetesNodeSummary`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself because it has a `DateTimeOffset` member - see `$lib/memorypack/date-time-offset.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class KubernetesNodeSummary {
	nodeName: string;
	clusterName: string | null;
	ready: boolean | null;
	cpuCores: number | null;
	cpuPercent: number | null;
	memoryWorkingSetBytes: number | null;
	memoryPercent: number | null;
	podCount: number | null;
	lastSeen: Date;

	constructor() {
		this.nodeName = '';
		this.clusterName = null;
		this.ready = null;
		this.cpuCores = null;
		this.cpuPercent = null;
		this.memoryWorkingSetBytes = null;
		this.memoryPercent = null;
		this.podCount = null;
		this.lastSeen = new Date(0);
	}

	static serialize(value: KubernetesNodeSummary | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesNodeSummary | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(9);
		writer.writeString(value.nodeName);
		writer.writeString(value.clusterName);
		writer.writeNullableBoolean(value.ready);
		writer.writeNullableFloat64(value.cpuCores);
		writer.writeNullableFloat64(value.cpuPercent);
		writer.writeNullableFloat64(value.memoryWorkingSetBytes);
		writer.writeNullableFloat64(value.memoryPercent);
		writer.writeNullableInt32(value.podCount);
		writeDateTimeOffset(writer, value.lastSeen);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesNodeSummary | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesNodeSummary | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesNodeSummary();
		if (count == 9) {
			value.nodeName = reader.readString() ?? '';
			value.clusterName = reader.readString();
			value.ready = reader.readNullableBoolean();
			value.cpuCores = reader.readNullableFloat64();
			value.cpuPercent = reader.readNullableFloat64();
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			value.memoryPercent = reader.readNullableFloat64();
			value.podCount = reader.readNullableInt32();
			value.lastSeen = readDateTimeOffset(reader);
		} else if (count > 9) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.nodeName = reader.readString() ?? '';
			if (count == 1) return value;
			value.clusterName = reader.readString();
			if (count == 2) return value;
			value.ready = reader.readNullableBoolean();
			if (count == 3) return value;
			value.cpuCores = reader.readNullableFloat64();
			if (count == 4) return value;
			value.cpuPercent = reader.readNullableFloat64();
			if (count == 5) return value;
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			if (count == 6) return value;
			value.memoryPercent = reader.readNullableFloat64();
			if (count == 7) return value;
			value.podCount = reader.readNullableInt32();
			if (count == 8) return value;
			value.lastSeen = readDateTimeOffset(reader);
		}
		return value;
	}
}
