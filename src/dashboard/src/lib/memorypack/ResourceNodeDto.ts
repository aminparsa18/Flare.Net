// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/ResourceGraphDto.cs`'s `ResourceNodeDto`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself: its `Urls`
// member is an `IReadOnlyList<string>` - see `PipelineServiceBreakdown.ts`'s header comment
// for why `IReadOnlyList<T>` alone blocks `[GenerateTypeScript]`, regardless of whether `T`
// is itself a primitive (confirmed by actually attaching the attribute here and hitting
// `MEMPACK031` on `Urls` specifically, before switching to hand-writing it). `state`/`health`
// are raw MemoryPack numeric ordinals (converted to string at `api.ts`'s module boundary via
// `$lib/memorypack/enums.ts`'s `resourceStateToString`/`resourceHealthToString`).

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class ResourceNodeDto {
	id: string | null;
	role: string | null;
	name: string | null;
	image: string | null;
	state: number;
	health: number | null;
	urls: (string | null)[] | null;
	kind: string | null;
	parentId: string | null;

	constructor() {
		this.id = null;
		this.role = null;
		this.name = null;
		this.image = null;
		this.state = 0;
		this.health = null;
		this.urls = null;
		this.kind = null;
		this.parentId = null;
	}

	static serialize(value: ResourceNodeDto | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: ResourceNodeDto | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(9);
		writer.writeString(value.id);
		writer.writeString(value.role);
		writer.writeString(value.name);
		writer.writeString(value.image);
		writer.writeInt32(value.state);
		writer.writeNullableInt32(value.health);
		writer.writeArray(value.urls, (writer, x) => writer.writeString(x));
		writer.writeString(value.kind);
		writer.writeString(value.parentId);
	}

	static serializeArray(value: (ResourceNodeDto | null)[] | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeArrayCore(writer, value);
		return writer.toArray();
	}

	static serializeArrayCore(writer: MemoryPackWriter, value: (ResourceNodeDto | null)[] | null): void {
		writer.writeArray(value, (writer, x) => ResourceNodeDto.serializeCore(writer, x));
	}

	static deserialize(buffer: ArrayBuffer): ResourceNodeDto | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): ResourceNodeDto | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new ResourceNodeDto();
		if (count == 9) {
			value.id = reader.readString();
			value.role = reader.readString();
			value.name = reader.readString();
			value.image = reader.readString();
			value.state = reader.readInt32();
			value.health = reader.readNullableInt32();
			value.urls = reader.readArray((reader) => reader.readString());
			value.kind = reader.readString();
			value.parentId = reader.readString();
		} else if (count > 9) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.id = reader.readString();
			if (count == 1) return value;
			value.role = reader.readString();
			if (count == 2) return value;
			value.name = reader.readString();
			if (count == 3) return value;
			value.image = reader.readString();
			if (count == 4) return value;
			value.state = reader.readInt32();
			if (count == 5) return value;
			value.health = reader.readNullableInt32();
			if (count == 6) return value;
			value.urls = reader.readArray((reader) => reader.readString());
			if (count == 7) return value;
			value.kind = reader.readString();
			if (count == 8) return value;
			value.parentId = reader.readString();
			if (count == 9) return value;
		}
		return value;
	}

	static deserializeArray(buffer: ArrayBuffer): (ResourceNodeDto | null)[] | null {
		return this.deserializeArrayCore(new MemoryPackReader(buffer));
	}

	static deserializeArrayCore(reader: MemoryPackReader): (ResourceNodeDto | null)[] | null {
		return reader.readArray((reader) => ResourceNodeDto.deserializeCore(reader));
	}
}
