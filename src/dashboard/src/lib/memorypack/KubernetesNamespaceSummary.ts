// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesWorkloadModels.cs`'s `KubernetesNamespaceSummary`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself because it has a `DateTimeOffset` member - see `$lib/memorypack/date-time-offset.ts`.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class KubernetesNamespaceSummary {
	namespace: string;
	phase: string | null;
	podCount: number | null;
	cpuCores: number | null;
	memoryWorkingSetBytes: number | null;
	lastSeen: Date;

	constructor() {
		this.namespace = '';
		this.phase = null;
		this.podCount = null;
		this.cpuCores = null;
		this.memoryWorkingSetBytes = null;
		this.lastSeen = new Date(0);
	}

	static serialize(value: KubernetesNamespaceSummary | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesNamespaceSummary | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeString(value.namespace);
		writer.writeString(value.phase);
		writer.writeNullableInt32(value.podCount);
		writer.writeNullableFloat64(value.cpuCores);
		writer.writeNullableFloat64(value.memoryWorkingSetBytes);
		writeDateTimeOffset(writer, value.lastSeen);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesNamespaceSummary | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesNamespaceSummary | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesNamespaceSummary();
		if (count == 6) {
			value.namespace = reader.readString() ?? '';
			value.phase = reader.readString();
			value.podCount = reader.readNullableInt32();
			value.cpuCores = reader.readNullableFloat64();
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			value.lastSeen = readDateTimeOffset(reader);
		} else if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.namespace = reader.readString() ?? '';
			if (count == 1) return value;
			value.phase = reader.readString();
			if (count == 2) return value;
			value.podCount = reader.readNullableInt32();
			if (count == 3) return value;
			value.cpuCores = reader.readNullableFloat64();
			if (count == 4) return value;
			value.memoryWorkingSetBytes = reader.readNullableFloat64();
			if (count == 5) return value;
			value.lastSeen = readDateTimeOffset(reader);
		}
		return value;
	}
}
