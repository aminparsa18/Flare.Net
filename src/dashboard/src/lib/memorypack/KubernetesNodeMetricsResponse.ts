// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesInventoryModels.cs`'s `KubernetesNodeMetricsResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself: its list is an `IReadOnlyList<T>` (see ADR-0016) of a hand-written type.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { KubernetesNodeMetricsPoint } from '$lib/memorypack/KubernetesNodeMetricsPoint';

export class KubernetesNodeMetricsResponse {
	nodeName: string;
	windowMinutes: number;
	bucketWidthSeconds: number;
	allocatableCpuCores: number | null;
	points: (KubernetesNodeMetricsPoint | null)[] | null;

	constructor() {
		this.nodeName = '';
		this.windowMinutes = 0;
		this.bucketWidthSeconds = 0;
		this.allocatableCpuCores = null;
		this.points = null;
	}

	static serialize(value: KubernetesNodeMetricsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesNodeMetricsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(5);
		writer.writeString(value.nodeName);
		writer.writeInt32(value.windowMinutes);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeNullableFloat64(value.allocatableCpuCores);
		writer.writeArray(value.points, (writer, x) => KubernetesNodeMetricsPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): KubernetesNodeMetricsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesNodeMetricsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesNodeMetricsResponse();
		if (count == 5) {
			value.nodeName = reader.readString() ?? '';
			value.windowMinutes = reader.readInt32();
			value.bucketWidthSeconds = reader.readInt32();
			value.allocatableCpuCores = reader.readNullableFloat64();
			value.points = reader.readArray((reader) => KubernetesNodeMetricsPoint.deserializeCore(reader));
		} else if (count > 5) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.nodeName = reader.readString() ?? '';
			if (count == 1) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 2) return value;
			value.bucketWidthSeconds = reader.readInt32();
			if (count == 3) return value;
			value.allocatableCpuCores = reader.readNullableFloat64();
			if (count == 4) return value;
			value.points = reader.readArray((reader) => KubernetesNodeMetricsPoint.deserializeCore(reader));
		}
		return value;
	}
}
