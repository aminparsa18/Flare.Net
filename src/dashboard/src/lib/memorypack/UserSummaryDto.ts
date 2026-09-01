// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/UserModels.cs`'s `UserSummaryDto` field-for-field,
// in the exact same declared order (MemoryPack's wire format is positional, not keyed - see
// this file's `serializeCore`/`deserializeCore`, which follow the identical
// writeObjectHeader/tryReadObjectHeader + per-field version-tolerant shape MemoryPack's own
// generator emits, e.g. `$lib/generated/memorypack/AuthUserDto.ts`). Can't carry
// `[GenerateTypeScript]` itself because `CreatedAt` is a `DateTimeOffset` - MemoryPack's
// TypeScript generator has no mapping for it (see `$lib/memorypack/date-time-offset.ts`'s
// header comment for the full explanation and the wire format this hand-writes against).
//
// If `UserModels.cs`'s `UserSummaryDto` ever gains/removes/reorders a member, this file
// must be updated by hand to match - there is no compiler/generator to catch drift the way
// there is for the 6 dashboard client files that use real generated classes.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class UserSummaryDto {
	id: string;
	username: string;
	role: number;
	authProvider: string;
	isDisabled: boolean;
	createdAt: Date;

	constructor() {
		this.id = '00000000-0000-0000-0000-000000000000';
		this.username = '';
		this.role = 0;
		this.authProvider = '';
		this.isDisabled = false;
		this.createdAt = new Date(0);
	}

	static serialize(value: UserSummaryDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: UserSummaryDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeGuid(value.id);
		writer.writeString(value.username);
		writer.writeInt32(value.role);
		writer.writeString(value.authProvider);
		writer.writeBoolean(value.isDisabled);
		writeDateTimeOffset(writer, value.createdAt);
	}

	static serializeArray(value: (UserSummaryDto | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (UserSummaryDto | null)[] | null): void {
		writer.writeArray(value, (writer, x) => UserSummaryDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): UserSummaryDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): UserSummaryDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new UserSummaryDto();
		if (count == 6) {
			value.id = reader.readGuid();
			value.username = reader.readString() ?? '';
			value.role = reader.readInt32();
			value.authProvider = reader.readString() ?? '';
			value.isDisabled = reader.readBoolean();
			value.createdAt = readDateTimeOffset(reader);
		} else if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.id = reader.readGuid();
			if (count == 1) return value;
			value.username = reader.readString() ?? '';
			if (count == 2) return value;
			value.role = reader.readInt32();
			if (count == 3) return value;
			value.authProvider = reader.readString() ?? '';
			if (count == 4) return value;
			value.isDisabled = reader.readBoolean();
			if (count == 5) return value;
			value.createdAt = readDateTimeOffset(reader);
			if (count == 6) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (UserSummaryDto | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (UserSummaryDto | null)[] | null {
		return reader.readArray((reader) => UserSummaryDto.deserializeCore(reader));
	}
}
