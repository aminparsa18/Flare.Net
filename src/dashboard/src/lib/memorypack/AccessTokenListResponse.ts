// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PersonalAccessTokenModels.cs`'s
// `AccessTokenListResponse`. Can't carry `[GenerateTypeScript]` itself because its one
// member's type, `AccessTokenDto`, has `DateTimeOffset` members - see
// `AccessTokenDto.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { AccessTokenDto } from '$lib/memorypack/AccessTokenDto';

export class AccessTokenListResponse {
	tokens: (AccessTokenDto | null)[] | null;

	constructor() {
		this.tokens = null;
	}

	static serialize(value: AccessTokenListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: AccessTokenListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.tokens, (writer, x) => AccessTokenDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): AccessTokenListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): AccessTokenListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new AccessTokenListResponse();
		if (count == 1) {
			value.tokens = reader.readArray((reader) => AccessTokenDto.deserializeCore(reader));
		} else if (count > 1) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.tokens = reader.readArray((reader) => AccessTokenDto.deserializeCore(reader));
		}
		return value;
	}
}
