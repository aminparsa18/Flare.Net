// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/DashboardModels.cs`'s `DashboardListResponse`.
// Can't carry `[GenerateTypeScript]` itself because its one member's type, `Dashboard`, has
// a `JsonElement` member - see `Dashboard.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { Dashboard } from '$lib/memorypack/Dashboard';

export class DashboardListResponse {
	dashboards: (Dashboard | null)[] | null;

	constructor() {
		this.dashboards = null;
	}

	static serialize(value: DashboardListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: DashboardListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.dashboards, (writer, x) => Dashboard.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): DashboardListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): DashboardListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new DashboardListResponse();
		if (count == 1) {
			value.dashboards = reader.readArray((reader) => Dashboard.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.dashboards = reader.readArray((reader) => Dashboard.deserializeCore(reader));
		}
		return value;
	}
}
