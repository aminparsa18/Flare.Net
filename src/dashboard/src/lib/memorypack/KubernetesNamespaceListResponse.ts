// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesWorkloadModels.cs`'s `KubernetesNamespaceListResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself: its list is an `IReadOnlyList<T>` (see ADR-0016) of a hand-written type.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { KubernetesNamespaceSummary } from '$lib/memorypack/KubernetesNamespaceSummary';

export class KubernetesNamespaceListResponse {
	windowMinutes: number;
	namespaces: (KubernetesNamespaceSummary | null)[] | null;
	truncated: boolean;

	constructor() {
		this.windowMinutes = 0;
		this.namespaces = null;
		this.truncated = false;
	}

	static serialize(value: KubernetesNamespaceListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesNamespaceListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.namespaces, (writer, x) => KubernetesNamespaceSummary.serializeCore(writer, x));
		writer.writeBoolean(value.truncated);
	}

	static deserialize(buffer: ArrayBuffer): KubernetesNamespaceListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesNamespaceListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesNamespaceListResponse();
		if (count == 3) {
			value.windowMinutes = reader.readInt32();
			value.namespaces = reader.readArray((reader) => KubernetesNamespaceSummary.deserializeCore(reader));
			value.truncated = reader.readBoolean();
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 1) return value;
			value.namespaces = reader.readArray((reader) => KubernetesNamespaceSummary.deserializeCore(reader));
			if (count == 2) return value;
			value.truncated = reader.readBoolean();
		}
		return value;
	}
}
