// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesWorkloadModels.cs`'s `KubernetesVolumeSummary`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself because it has a `DateTimeOffset` member - see `$lib/memorypack/date-time-offset.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class KubernetesVolumeSummary {
	volumeName: string;
	namespace: string;
	podName: string;
	volumeType: string | null;
	claimName: string | null;
	capacityBytes: number | null;
	availableBytes: number | null;
	usedBytes: number | null;
	usedPercent: number | null;
	inodesUsedPercent: number | null;
	lastSeen: Date;

	constructor() {
		this.volumeName = '';
		this.namespace = '';
		this.podName = '';
		this.volumeType = null;
		this.claimName = null;
		this.capacityBytes = null;
		this.availableBytes = null;
		this.usedBytes = null;
		this.usedPercent = null;
		this.inodesUsedPercent = null;
		this.lastSeen = new Date(0);
	}

	static serialize(value: KubernetesVolumeSummary | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesVolumeSummary | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(11);
		writer.writeString(value.volumeName);
		writer.writeString(value.namespace);
		writer.writeString(value.podName);
		writer.writeString(value.volumeType);
		writer.writeString(value.claimName);
		writer.writeNullableFloat64(value.capacityBytes);
		writer.writeNullableFloat64(value.availableBytes);
		writer.writeNullableFloat64(value.usedBytes);
		writer.writeNullableFloat64(value.usedPercent);
		writer.writeNullableFloat64(value.inodesUsedPercent);
		writeDateTimeOffset(writer, value.lastSeen);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesVolumeSummary | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesVolumeSummary | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesVolumeSummary();
		if (count == 11) {
			value.volumeName = reader.readString() ?? '';
			value.namespace = reader.readString() ?? '';
			value.podName = reader.readString() ?? '';
			value.volumeType = reader.readString();
			value.claimName = reader.readString();
			value.capacityBytes = reader.readNullableFloat64();
			value.availableBytes = reader.readNullableFloat64();
			value.usedBytes = reader.readNullableFloat64();
			value.usedPercent = reader.readNullableFloat64();
			value.inodesUsedPercent = reader.readNullableFloat64();
			value.lastSeen = readDateTimeOffset(reader);
		} else if (count > 11) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.volumeName = reader.readString() ?? '';
			if (count == 1) return value;
			value.namespace = reader.readString() ?? '';
			if (count == 2) return value;
			value.podName = reader.readString() ?? '';
			if (count == 3) return value;
			value.volumeType = reader.readString();
			if (count == 4) return value;
			value.claimName = reader.readString();
			if (count == 5) return value;
			value.capacityBytes = reader.readNullableFloat64();
			if (count == 6) return value;
			value.availableBytes = reader.readNullableFloat64();
			if (count == 7) return value;
			value.usedBytes = reader.readNullableFloat64();
			if (count == 8) return value;
			value.usedPercent = reader.readNullableFloat64();
			if (count == 9) return value;
			value.inodesUsedPercent = reader.readNullableFloat64();
			if (count == 10) return value;
			value.lastSeen = readDateTimeOffset(reader);
		}
		return value;
	}
}
