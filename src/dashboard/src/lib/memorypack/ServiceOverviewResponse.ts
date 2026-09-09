// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ServiceOverviewModels.cs`'s
// `ServiceOverviewResponse` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself: it nests `IReadOnlyList<ServiceMetrics>`, which blocks
// the generator the same way `IndexingStatsResponse`'s list members do (see that file's
// own header comment). `ServiceMetrics` has no such member, so it's a real generated
// class, reused here directly.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ServiceMetrics } from '$lib/generated/memorypack/ServiceMetrics.js';

export class ServiceOverviewResponse {
	windowMinutes: number;
	services: (ServiceMetrics | null)[] | null;

	constructor() {
		this.windowMinutes = 0;
		this.services = null;
	}

	static serialize(value: ServiceOverviewResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ServiceOverviewResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.services, (writer, x) => ServiceMetrics.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ServiceOverviewResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ServiceOverviewResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ServiceOverviewResponse();
		if (count == 2) {
			value.windowMinutes = reader.readInt32();
			value.services = reader.readArray((reader) => ServiceMetrics.deserializeCore(reader));
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.windowMinutes = reader.readInt32();
			if (count == 1) return value;
			value.services = reader.readArray((reader) => ServiceMetrics.deserializeCore(reader));
			if (count == 2) return value;
		}
		return value;
	}
}
