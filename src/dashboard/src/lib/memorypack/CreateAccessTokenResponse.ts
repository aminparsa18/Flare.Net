// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/PersonalAccessTokenModels.cs`'s
// `CreateAccessTokenResponse`. Can't carry `[GenerateTypeScript]` itself because its
// `Token` member's type, `AccessTokenDto`, has `DateTimeOffset` members - see
// `AccessTokenDto.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { AccessTokenDto } from '$lib/memorypack/AccessTokenDto';

export class CreateAccessTokenResponse {
	token: AccessTokenDto | null;
	rawToken: string | null;

	constructor() {
		this.token = null;
		this.rawToken = null;
	}

	static serialize(value: CreateAccessTokenResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: CreateAccessTokenResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(2);
		AccessTokenDto.serializeCore(writer, value.token);
		writer.writeString(value.rawToken);
	}

	static deserialize(buffer: ArrayBuffer): CreateAccessTokenResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): CreateAccessTokenResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new CreateAccessTokenResponse();
		if (count == 2) {
			value.token = AccessTokenDto.deserializeCore(reader);
			value.rawToken = reader.readString();
		} else if (count > 2) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.token = AccessTokenDto.deserializeCore(reader);
			if (count == 1) return value;
			value.rawToken = reader.readString();
		}
		return value;
	}
}
