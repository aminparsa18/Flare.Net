// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ExternalApiModels.cs`'s
// `ExternalDomainDetailResponse`, in declared order. Same `IReadOnlyList<T>` reason as
// `ExternalDomainsResponse.ts`; every row type is generated.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ExternalEndpointStats } from '$lib/generated/memorypack/ExternalEndpointStats.js';
import { ExternalStatusCodeCount } from '$lib/generated/memorypack/ExternalStatusCodeCount.js';
import { ExternalCallerStats } from '$lib/generated/memorypack/ExternalCallerStats.js';
import { ExternalErrorGroup } from '$lib/generated/memorypack/ExternalErrorGroup.js';

export class ExternalDomainDetailResponse {
	domain: string | null;
	windowMinutes: number;
	endpoints: (ExternalEndpointStats | null)[] | null;
	statusCodes: (ExternalStatusCodeCount | null)[] | null;
	callers: (ExternalCallerStats | null)[] | null;
	topErrors: (ExternalErrorGroup | null)[] | null;

	constructor() {
		this.domain = null;
		this.windowMinutes = 0;
		this.endpoints = null;
		this.statusCodes = null;
		this.callers = null;
		this.topErrors = null;
	}

	static serialize(value: ExternalDomainDetailResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExternalDomainDetailResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeString(value.domain);
		writer.writeInt32(value.windowMinutes);
		writer.writeArray(value.endpoints, (writer, x) => ExternalEndpointStats.serializeCore(writer, x));
		writer.writeArray(value.statusCodes, (writer, x) => ExternalStatusCodeCount.serializeCore(writer, x));
		writer.writeArray(value.callers, (writer, x) => ExternalCallerStats.serializeCore(writer, x));
		writer.writeArray(value.topErrors, (writer, x) => ExternalErrorGroup.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ExternalDomainDetailResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExternalDomainDetailResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExternalDomainDetailResponse();
		if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		}
		if (count == 0) return value;
		value.domain = reader.readString();
		if (count == 1) return value;
		value.windowMinutes = reader.readInt32();
		if (count == 2) return value;
		value.endpoints = reader.readArray((reader) => ExternalEndpointStats.deserializeCore(reader));
		if (count == 3) return value;
		value.statusCodes = reader.readArray((reader) => ExternalStatusCodeCount.deserializeCore(reader));
		if (count == 4) return value;
		value.callers = reader.readArray((reader) => ExternalCallerStats.deserializeCore(reader));
		if (count == 5) return value;
		value.topErrors = reader.readArray((reader) => ExternalErrorGroup.deserializeCore(reader));
		return value;
	}
}
