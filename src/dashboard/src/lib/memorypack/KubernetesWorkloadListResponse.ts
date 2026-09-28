// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesWorkloadModels.cs`'s `KubernetesWorkloadListResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself: its list is an `IReadOnlyList<T>` (see ADR-0016) of a hand-written type.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { KubernetesWorkloadSummary } from '$lib/memorypack/KubernetesWorkloadSummary';

export class KubernetesWorkloadListResponse {
	kind: string;
	windowMinutes: number;
	workloads: (KubernetesWorkloadSummary | null)[] | null;
	truncated: boolean;

	constructor() {
		this.kind = '';
		this.windowMinutes = 0;
		this.workloads = null;
		this.truncated = false;
	}

	static serialize(value: KubernetesWorkloadListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesWorkloadListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.kind);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.workloads, (writer, x) => KubernetesWorkloadSummary.serializeCore(writer, x));
		writer.writeBoolean(value.truncated);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesWorkloadListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesWorkloadListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesWorkloadListResponse();
		if (count == 4) {
			value.kind = reader.readString() ?? '';
			value.windowMinutes = reader.readInt32();
			value.workloads = reader.readArray((reader) => KubernetesWorkloadSummary.deserializeCore(reader));
			value.truncated = reader.readBoolean();
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.kind = reader.readString() ?? '';
			if (count == 1) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 2) return value;
			value.workloads = reader.readArray((reader) => KubernetesWorkloadSummary.deserializeCore(reader));
			if (count == 3) return value;
			value.truncated = reader.readBoolean();
		}
		return value;
	}
}
