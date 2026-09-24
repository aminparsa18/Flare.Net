// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/IngestApiKeyModels.cs`'s
// `IngestApiKeyListResponse`. Can't carry `[GenerateTypeScript]` itself because its one
// member's type, `IngestApiKeyDto`, has `DateTimeOffset` members - see
// `IngestApiKeyDto.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { IngestApiKeyDto } from '$lib/memorypack/IngestApiKeyDto';

export class IngestApiKeyListResponse {
	keys: (IngestApiKeyDto | null)[] | null;

	constructor() {
		this.keys = null;
	}

	static serialize(value: IngestApiKeyListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: IngestApiKeyListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.keys, (writer, x) => IngestApiKeyDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): IngestApiKeyListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): IngestApiKeyListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new IngestApiKeyListResponse();
		if (count == 1) {
			value.keys = reader.readArray((reader) => IngestApiKeyDto.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.keys = reader.readArray((reader) => IngestApiKeyDto.deserializeCore(reader));
		}
		return value;
	}
}
