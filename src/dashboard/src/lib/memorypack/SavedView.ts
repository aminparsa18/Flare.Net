// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SavedViewModels.cs`'s `SavedView` field-for-field,
// in declared order. Can't carry `[GenerateTypeScript]` itself for two independent reasons:
// `CreatedAt`/`UpdatedAt` are `DateTimeOffset` (see `$lib/memorypack/date-time-offset.ts`'s
// header comment), and `State` is a `System.Text.Json.JsonElement` - MemoryPack has no
// native `JsonElement` support at all (its TypeScript generator doesn't even have a
// not-yet-implemented case for it the way it does for `DateTimeOffset`; JsonElement isn't
// one of MemoryPack's own types). Server-side, `State` round-trips through a hand-written
// `MemoryPackFormatter<JsonElement>` (`src/Flare.Api/Json/JsonElementMemoryPackFormatter.cs`)
// that writes it as raw JSON text via a plain MemoryPack string - this file does exactly the
// same thing: `JSON.stringify`/`JSON.parse` around a `writeString`/`readString()` call,
// preserving the existing "opaque, unparsed-by-Flare.Api" contract (`state: unknown` here,
// same as the hand-mirrored JSON-era interface).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';
import { readDateTimeOffset, writeDateTimeOffset } from '$lib/memorypack/date-time-offset';

export class SavedView {
	id: string;
	name: string | null;
	description: string | null;
	pageType: number;
	state: unknown;
	createdAt: Date;
	updatedAt: Date;

	constructor() {
		this.id = '00000000-0000-0000-0000-000000000000';
		this.name = null;
		this.description = null;
		this.pageType = 0;
		this.state = null;
		this.createdAt = new Date(0);
		this.updatedAt = new Date(0);
	}

	static serialize(value: SavedView | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SavedView | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(7);
		writer.writeGuid(value.id);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeInt32(value.pageType);
		writer.writeString(JSON.stringify(value.state ?? null));
		writeDateTimeOffset(writer, value.createdAt);
		writeDateTimeOffset(writer, value.updatedAt);
	}

	static serializeArray(value: (SavedView | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (SavedView | null)[] | null): void {
		writer.writeArray(value, (writer, x) => SavedView.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): SavedView | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SavedView | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SavedView();
		if (count == 7) {
			value.id = reader.readGuid();
			value.name = reader.readString();
			value.description = reader.readString();
			value.pageType = reader.readInt32();
			value.state = JSON.parse(reader.readString() ?? 'null');
			value.createdAt = readDateTimeOffset(reader);
			value.updatedAt = readDateTimeOffset(reader);
		} else if (count > 7) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.id = reader.readGuid();
			if (count == 1) return value;
			value.name = reader.readString();
			if (count == 2) return value;
			value.description = reader.readString();
			if (count == 3) return value;
			value.pageType = reader.readInt32();
			if (count == 4) return value;
			value.state = JSON.parse(reader.readString() ?? 'null');
			if (count == 5) return value;
			value.createdAt = readDateTimeOffset(reader);
			if (count == 6) return value;
			value.updatedAt = readDateTimeOffset(reader);
			if (count == 7) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (SavedView | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (SavedView | null)[] | null {
		return reader.readArray((reader) => SavedView.deserializeCore(reader));
	}
}
