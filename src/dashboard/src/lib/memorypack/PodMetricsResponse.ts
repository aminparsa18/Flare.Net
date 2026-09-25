// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PodMetricsModels.cs`'s `PodMetricsResponse`,
// in declared order. Can't carry `[GenerateTypeScript]` itself: `Points` is an
// `IReadOnlyList<T>` (see ADR-0016) of `PodMetricsPoint`, which is hand-written too.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { PodMetricsPoint } from '$lib/memorypack/PodMetricsPoint';

export class PodMetricsResponse {
	podName: string;
	windowMinutes: number;
	bucketWidthSeconds: number;
	points: (PodMetricsPoint | null)[] | null;

	constructor() {
		this.podName = '';
		this.windowMinutes = 0;
		this.bucketWidthSeconds = 0;
		this.points = null;
	}

	static serialize(value: PodMetricsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: PodMetricsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.podName);
		writer.writeInt32(value.windowMinutes);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeArray(value.points, (writer, x) => PodMetricsPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): PodMetricsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): PodMetricsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new PodMetricsResponse();
		if (count == 4) {
			value.podName = reader.readString() ?? '';
			value.windowMinutes = reader.readInt32();
			value.bucketWidthSeconds = reader.readInt32();
			value.points = reader.readArray((reader) => PodMetricsPoint.deserializeCore(reader));
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.podName = reader.readString() ?? '';
			if (count == 1) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 2) return value;
			value.bucketWidthSeconds = reader.readInt32();
			if (count == 3) return value;
			value.points = reader.readArray((reader) => PodMetricsPoint.deserializeCore(reader));
		}
		return value;
	}
}
