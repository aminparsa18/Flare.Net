// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/KubernetesWorkloadModels.cs`'s `KubernetesWorkloadMetricsResponse`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]`
// itself: its list is an `IReadOnlyList<T>` (see ADR-0016) of a hand-written type.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { KubernetesWorkloadMetricsPoint } from '$lib/memorypack/KubernetesWorkloadMetricsPoint';

export class KubernetesWorkloadMetricsResponse {
	kind: string;
	namespace: string;
	name: string;
	windowMinutes: number;
	bucketWidthSeconds: number;
	points: (KubernetesWorkloadMetricsPoint | null)[] | null;

	constructor() {
		this.kind = '';
		this.namespace = '';
		this.name = '';
		this.windowMinutes = 0;
		this.bucketWidthSeconds = 0;
		this.points = null;
	}

	static serialize(value: KubernetesWorkloadMetricsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: KubernetesWorkloadMetricsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeString(value.kind);
		writer.writeString(value.namespace);
		writer.writeString(value.name);
		writer.writeInt32(value.windowMinutes);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeArray(value.points, (writer, x) => KubernetesWorkloadMetricsPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): KubernetesWorkloadMetricsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): KubernetesWorkloadMetricsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new KubernetesWorkloadMetricsResponse();
		if (count == 6) {
			value.kind = reader.readString() ?? '';
			value.namespace = reader.readString() ?? '';
			value.name = reader.readString() ?? '';
			value.windowMinutes = reader.readInt32();
			value.bucketWidthSeconds = reader.readInt32();
			value.points = reader.readArray((reader) => KubernetesWorkloadMetricsPoint.deserializeCore(reader));
		} else if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.kind = reader.readString() ?? '';
			if (count == 1) return value;
			value.namespace = reader.readString() ?? '';
			if (count == 2) return value;
			value.name = reader.readString() ?? '';
			if (count == 3) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 4) return value;
			value.bucketWidthSeconds = reader.readInt32();
			if (count == 5) return value;
			value.points = reader.readArray((reader) => KubernetesWorkloadMetricsPoint.deserializeCore(reader));
		}
		return value;
	}
}
