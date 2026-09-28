// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ExternalApiModels.cs`'s `ExternalDomainsResponse`,
// in declared order. Can't carry `[GenerateTypeScript]` itself: its members are
// `IReadOnlyList<T>` (see ADR-0016). `ExternalDomain` itself is generated.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ExternalDomain } from '$lib/generated/memorypack/ExternalDomain.js';

export class ExternalDomainsResponse {
	windowMinutes: number;
	domains: (ExternalDomain | null)[] | null;
	services: (string | null)[] | null;

	constructor() {
		this.windowMinutes = 0;
		this.domains = null;
		this.services = null;
	}

	static serialize(value: ExternalDomainsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExternalDomainsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(3);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.domains, (writer, x) => ExternalDomain.serializeCore(writer, x));
		writer.writeArray(value.services, (writer, x) => writer.writeString(x));
	}

	static deserialize(buffer: ArrayBuffer): ExternalDomainsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExternalDomainsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExternalDomainsResponse();
		if (count > 3) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		}
		if (count == 0) return value;
		value.windowMinutes = reader.readInt32();
		if (count == 1) return value;
		value.domains = reader.readArray((reader) => ExternalDomain.deserializeCore(reader));
		if (count == 2) return value;
		value.services = reader.readArray((reader) => reader.readString());
		return value;
	}
}
