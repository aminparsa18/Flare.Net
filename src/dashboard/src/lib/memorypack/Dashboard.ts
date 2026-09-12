// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/DashboardModels.cs`'s `Dashboard` field-for-field,
// in declared order. Can't carry `[GenerateTypeScript]` itself for the same two reasons
// `SavedView.ts`'s header comment gives: `CreatedAt`/`UpdatedAt` are `DateTimeOffset`
// (see `$lib/memorypack/date-time-offset.ts`), and `LayoutJson` is a
// `System.Text.Json.JsonElement`, round-tripped server-side through the same
// `JsonElementMemoryPackFormatter` `SavedView.State` uses - this file mirrors that exact
// wire format (`JSON.stringify`/`JSON.parse` around a plain MemoryPack string).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class Dashboard {
	id: string;
	name: string | null;
	description: string | null;
	layoutJson: unknown;
	createdAt: Date;
	updatedAt: Date;

	constructor() {
		this.id = '00000000-0000-0000-0000-000000000000';
		this.name = null;
		this.description = null;
		this.layoutJson = null;
		this.createdAt = new Date(0);
		this.updatedAt = new Date(0);
	}

	static serialize(value: Dashboard | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: Dashboard | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(6);
		writer.writeGuid(value.id);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeString(JSON.stringify(value.layoutJson ?? null));
		writeDateTimeOffset(writer, value.createdAt);
		writeDateTimeOffset(writer, value.updatedAt);
	}

	static serializeArray(value: (Dashboard | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (Dashboard | null)[] | null): void {
		writer.writeArray(value, (writer, x) => Dashboard.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): Dashboard | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): Dashboard | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new Dashboard();
		if (count == 6) {
			value.id = reader.readGuid();
			value.name = reader.readString();
			value.description = reader.readString();
			value.layoutJson = JSON.parse(reader.readString() ?? 'null');
			value.createdAt = readDateTimeOffset(reader);
			value.updatedAt = readDateTimeOffset(reader);
		} else if (count > 6) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.id = reader.readGuid();
			if (count == 1) return value;
			value.name = reader.readString();
			if (count == 2) return value;
			value.description = reader.readString();
			if (count == 3) return value;
			value.layoutJson = JSON.parse(reader.readString() ?? 'null');
			if (count == 4) return value;
			value.createdAt = readDateTimeOffset(reader);
			if (count == 5) return value;
			value.updatedAt = readDateTimeOffset(reader);
			if (count == 6) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (Dashboard | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (Dashboard | null)[] | null {
		return reader.readArray((reader) => Dashboard.deserializeCore(reader));
	}
}
