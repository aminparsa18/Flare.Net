// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ErrorModels.cs`'s `ExceptionGroupsResponse`
// field-for-field. Can't carry `[GenerateTypeScript]` itself because an
// `IReadOnlyList<ExceptionGroup>` member blocks the generator - same
// `ServiceOverviewResponse`/`SpanSearchResponse` precedent.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { ExceptionGroup } from '$lib/memorypack/ExceptionGroup';

export class ExceptionGroupsResponse {
	groups: (ExceptionGroup | null)[] | null;

	constructor() {
		this.groups = null;
	}

	static serialize(value: ExceptionGroupsResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ExceptionGroupsResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.groups, (writer, x) => ExceptionGroup.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ExceptionGroupsResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ExceptionGroupsResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ExceptionGroupsResponse();
		if (count == 1) {
			value.groups = reader.readArray((reader) => ExceptionGroup.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.groups = reader.readArray((reader) => ExceptionGroup.deserializeCore(reader));
			if (count == 1) return value;
		}
		return value;
	}
}
