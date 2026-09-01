// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/UserModels.cs`'s `UserListResponse`. Can't carry
// `[GenerateTypeScript]` itself because its one member's type, `UserSummaryDto`, has a
// `DateTimeOffset` member - see `UserSummaryDto.ts`'s header comment.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { UserSummaryDto } from '$lib/memorypack/UserSummaryDto';

export class UserListResponse {
	users: (UserSummaryDto | null)[] | null;

	constructor() {
		this.users = null;
	}

	static serialize(value: UserListResponse | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: UserListResponse | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(1);
		writer.writeArray(value.users, (writer, x) => UserSummaryDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): UserListResponse | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): UserListResponse | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new UserListResponse();
		if (count == 0) return value;
		value.users = reader.readArray((reader) => UserSummaryDto.deserializeCore(reader));
		return value;
	}
}
