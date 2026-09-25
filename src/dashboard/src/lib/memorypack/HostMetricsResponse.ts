// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/HostInventoryModels.cs`'s `HostMetricsResponse`,
// in declared order. Can't carry `[GenerateTypeScript]` itself: `Points` is an
// `IReadOnlyList<T>` (see ADR-0016) of `HostMetricsPoint`, which is hand-written too.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { HostMetricsPoint } from '$lib/memorypack/HostMetricsPoint';

export class HostMetricsResponse {
	hostName: string;
	windowMinutes: number;
	bucketWidthSeconds: number;
	points: (HostMetricsPoint | null)[] | null;

	constructor() {
		this.hostName = '';
		this.windowMinutes = 0;
		this.bucketWidthSeconds = 0;
		this.points = null;
	}

	static serialize(value: HostMetricsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: HostMetricsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.hostName);
		writer.writeInt32(value.windowMinutes);
		writer.writeInt32(value.bucketWidthSeconds);
		writer.writeArray(value.points, (writer, x) => HostMetricsPoint.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): HostMetricsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): HostMetricsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new HostMetricsResponse();
		if (count == 4) {
			value.hostName = reader.readString() ?? '';
			value.windowMinutes = reader.readInt32();
			value.bucketWidthSeconds = reader.readInt32();
			value.points = reader.readArray((reader) => HostMetricsPoint.deserializeCore(reader));
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.hostName = reader.readString() ?? '';
			if (count == 1) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 2) return value;
			value.bucketWidthSeconds = reader.readInt32();
			if (count == 3) return value;
			value.points = reader.readArray((reader) => HostMetricsPoint.deserializeCore(reader));
		}
		return value;
	}
}
