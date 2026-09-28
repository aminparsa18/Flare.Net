// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesWorkloadModels.cs`'s `KubernetesVolumeMetricsResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself: its list is an `IReadOnlyList<T>` (see ADR-0016) of a hand-written type.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { KubernetesVolumeMetricsPoint } from '$lib/memorypack/KubernetesVolumeMetricsPoint';

export class KubernetesVolumeMetricsResponse {
	namespace: string;
	podName: string;
	volumeName: string;
	windowMinutes: number;
	bucketWidthSeconds: number;
	points: (KubernetesVolumeMetricsPoint | null)[] | null;

	constructor() {
		this.namespace = '';
		this.podName = '';
		this.volumeName = '';
		this.windowMinutes = 0;
		this.bucketWidthSeconds = 0;
		this.points = null;
	}

	static serialize(value: KubernetesVolumeMetricsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesVolumeMetricsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeString(value.namespace);
		writer.writeString(value.podName);
		writer.writeString(value.volumeName);
		writer.writeInt32(value.windowMinutes);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeArray(value.points, (writer, x) => KubernetesVolumeMetricsPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): KubernetesVolumeMetricsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesVolumeMetricsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesVolumeMetricsResponse();
		if (count == 6) {
			value.namespace = reader.readString() ?? '';
			value.podName = reader.readString() ?? '';
			value.volumeName = reader.readString() ?? '';
			value.windowMinutes = reader.readInt32();
			value.bucketWidthSeconds = reader.readInt32();
			value.points = reader.readArray((reader) => KubernetesVolumeMetricsPoint.deserializeCore(reader));
		} else if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.namespace = reader.readString() ?? '';
			if (count == 1) return value;
			value.podName = reader.readString() ?? '';
			if (count == 2) return value;
			value.volumeName = reader.readString() ?? '';
			if (count == 3) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 4) return value;
			value.bucketWidthSeconds = reader.readInt32();
			if (count == 5) return value;
			value.points = reader.readArray((reader) => KubernetesVolumeMetricsPoint.deserializeCore(reader));
		}
		return value;
	}
}
