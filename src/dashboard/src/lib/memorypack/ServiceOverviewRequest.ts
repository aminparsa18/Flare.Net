// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ServiceOverviewModels.cs`'s
// `ServiceOverviewRequest` field-for-field, in declared order. Can't carry
// `[GenerateTypeScript]` itself because `resourceAttributes` is a list member - see
// `ResourceAttributeFilter`'s own header comment on the C# side for why the *element*
// type is generated even though this wrapping list is not.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ResourceAttributeFilter } from '$lib/generated/memorypack/ResourceAttributeFilter.js';

export class ServiceOverviewRequest {
	windowMinutes: number | null;
	resourceAttributes: (ResourceAttributeFilter | null)[] | null;

	constructor() {
		this.windowMinutes = null;
		this.resourceAttributes = null;
	}

	static serialize(value: ServiceOverviewRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ServiceOverviewRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		writer.writeNullableInt32(value.windowMinutes);
		writer.writeArray(value.resourceAttributes, (writer, x) => ResourceAttributeFilter.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ServiceOverviewRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ServiceOverviewRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ServiceOverviewRequest();
		if (count == 2) {
			value.windowMinutes = reader.readNullableInt32();
			value.resourceAttributes = reader.readArray((reader) => ResourceAttributeFilter.deserializeCore(reader));
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.windowMinutes = reader.readNullableInt32();
			if (count == 1) return value;
			value.resourceAttributes = reader.readArray((reader) => ResourceAttributeFilter.deserializeCore(reader));
			if (count == 2) return value;
		}
		return value;
	}
}
