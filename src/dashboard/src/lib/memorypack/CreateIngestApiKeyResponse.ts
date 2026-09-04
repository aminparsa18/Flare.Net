// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/IngestApiKeyModels.cs`'s `CreateIngestApiKeyResponse`.
// Can't carry `[GenerateTypeScript]` itself because its `Key` member's type,
// `IngestApiKeyDto`, has a `DateTimeOffset` member - see `IngestApiKeyDto.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { IngestApiKeyDto } from '$lib/memorypack/IngestApiKeyDto';

export class CreateIngestApiKeyResponse {
	key: IngestApiKeyDto | null;
	rawKey: string | null;

	constructor() {
		this.key = null;
		this.rawKey = null;
	}

	static serialize(value: CreateIngestApiKeyResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: CreateIngestApiKeyResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		IngestApiKeyDto.serializeCore(writer, value.key);
		writer.writeString(value.rawKey);
	}

	static deserialize(buffer: ArrayBuffer): CreateIngestApiKeyResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): CreateIngestApiKeyResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new CreateIngestApiKeyResponse();
		if (count == 2) {
			value.key = IngestApiKeyDto.deserializeCore(reader);
			value.rawKey = reader.readString();
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.key = IngestApiKeyDto.deserializeCore(reader);
			if (count == 1) return value;
			value.rawKey = reader.readString();
		}
		return value;
	}
}
