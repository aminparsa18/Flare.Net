// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesInventoryModels.cs`'s `KubernetesPodListResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself: its list is an `IReadOnlyList<T>` (see ADR-0016) of a hand-written type.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { KubernetesPodSummary } from '$lib/memorypack/KubernetesPodSummary';

export class KubernetesPodListResponse {
	windowMinutes: number;
	pods: (KubernetesPodSummary | null)[] | null;
	truncated: boolean;

	constructor() {
		this.windowMinutes = 0;
		this.pods = null;
		this.truncated = false;
	}

	static serialize(value: KubernetesPodListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesPodListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.pods, (writer, x) => KubernetesPodSummary.serializeCore(writer, x));
		writer.writeBoolean(value.truncated);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesPodListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesPodListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesPodListResponse();
		if (count == 3) {
			value.windowMinutes = reader.readInt32();
			value.pods = reader.readArray((reader) => KubernetesPodSummary.deserializeCore(reader));
			value.truncated = reader.readBoolean();
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 1) return value;
			value.pods = reader.readArray((reader) => KubernetesPodSummary.deserializeCore(reader));
			if (count == 2) return value;
			value.truncated = reader.readBoolean();
		}
		return value;
	}
}
