// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/HostInventoryModels.cs`'s `HostListResponse`,
// in declared order. Can't carry `[GenerateTypeScript]` itself: `Hosts` is an
// `IReadOnlyList<T>` (see ADR-0016) of `HostSummary`, which is hand-written too.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { HostSummary } from '$lib/memorypack/HostSummary';

export class HostListResponse {
	windowMinutes: number;
	hosts: (HostSummary | null)[] | null;
	truncated: boolean;

	constructor() {
		this.windowMinutes = 0;
		this.hosts = null;
		this.truncated = false;
	}

	static serialize(value: HostListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: HostListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.hosts, (writer, x) => HostSummary.serializeCore(writer, x));
		writer.writeBoolean(value.truncated);
	}

	static deserialize(buffer: ArrayBuffer): HostListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): HostListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new HostListResponse();
		if (count == 3) {
			value.windowMinutes = reader.readInt32();
			value.hosts = reader.readArray((reader) => HostSummary.deserializeCore(reader));
			value.truncated = reader.readBoolean();
		} else if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 1) return value;
			value.hosts = reader.readArray((reader) => HostSummary.deserializeCore(reader));
			if (count == 2) return value;
			value.truncated = reader.readBoolean();
		}
		return value;
	}
}
