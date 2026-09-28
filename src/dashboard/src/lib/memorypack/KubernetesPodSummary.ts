// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesInventoryModels.cs`'s `KubernetesPodSummary`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself because it has a `DateTimeOffset` member - see `$lib/memorypack/date-time-offset.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class KubernetesPodSummary {
	podName: string;
	namespace: string;
	nodeName: string | null;
	workloadKind: string | null;
	workloadName: string | null;
	phase: string | null;
	restarts: number | null;
	cpuCores: number | null;
	memoryWorkingSetBytes: number | null;
	cpuLimitPercent: number | null;
	memoryLimitPercent: number | null;
	lastSeen: Date;

	constructor() {
		this.podName = '';
		this.namespace = '';
		this.nodeName = null;
		this.workloadKind = null;
		this.workloadName = null;
		this.phase = null;
		this.restarts = null;
		this.cpuCores = null;
		this.memoryWorkingSetBytes = null;
		this.cpuLimitPercent = null;
		this.memoryLimitPercent = null;
		this.lastSeen = new Date(0);
	}

	static serialize(value: KubernetesPodSummary | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesPodSummary | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(12);
		writer.writeString(value.podName);
		writer.writeString(value.namespace);
		writer.writeString(value.nodeName);
		writer.writeString(value.workloadKind);
		writer.writeString(value.workloadName);
		writer.writeString(value.phase);
		writer.writeNullableInt32(value.restarts);
		writer.writeNullableFloat64(value.cpuCores);
		writer.writeNullableFloat64(value.memoryWorkingSetBytes);
		writer.writeNullableFloat64(value.cpuLimitPercent);
		writer.writeNullableFloat64(value.memoryLimitPercent);
		writeDateTimeOffset(writer, value.lastSeen);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesPodSummary | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesPodSummary | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesPodSummary();
		if (count == 12) {
			value.podName = reader.readString() ?? '';
			value.namespace = reader.readString() ?? '';
			value.nodeName = reader.readString();
			value.workloadKind = reader.readString();
			value.workloadName = reader.readString();
			value.phase = reader.readString();
			value.restarts = reader.readNullableInt32();
			value.cpuCores = reader.readNullableFloat64();
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			value.cpuLimitPercent = reader.readNullableFloat64();
			value.memoryLimitPercent = reader.readNullableFloat64();
			value.lastSeen = readDateTimeOffset(reader);
		} else if (count > 12) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.podName = reader.readString() ?? '';
			if (count == 1) return value;
			value.namespace = reader.readString() ?? '';
			if (count == 2) return value;
			value.nodeName = reader.readString();
			if (count == 3) return value;
			value.workloadKind = reader.readString();
			if (count == 4) return value;
			value.workloadName = reader.readString();
			if (count == 5) return value;
			value.phase = reader.readString();
			if (count == 6) return value;
			value.restarts = reader.readNullableInt32();
			if (count == 7) return value;
			value.cpuCores = reader.readNullableFloat64();
			if (count == 8) return value;
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			if (count == 9) return value;
			value.cpuLimitPercent = reader.readNullableFloat64();
			if (count == 10) return value;
			value.memoryLimitPercent = reader.readNullableFloat64();
			if (count == 11) return value;
			value.lastSeen = readDateTimeOffset(reader);
		}
		return value;
	}
}
