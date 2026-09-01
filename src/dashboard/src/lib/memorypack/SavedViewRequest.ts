// Hand-written companion to MemoryPack's generated TypeScript classes - NOT itself
// generated. Mirrors `src/Flare.Api/Model/SavedViewModels.cs`'s `SavedViewRequest`
// field-for-field, in declared order. Can't carry `[GenerateTypeScript]` itself because
// `State` is a `JsonElement` - see `SavedView.ts`'s header comment for the full explanation
// and the raw-JSON-text wire format this hand-writes against.

import { MemoryPackWriter } from '$lib/generated/memorypack/MemoryPackWriter.js';
import { MemoryPackReader } from '$lib/generated/memorypack/MemoryPackReader.js';

export class SavedViewRequest {
	name: string | null;
	description: string | null;
	pageType: number;
	state: unknown;

	constructor() {
		this.name = null;
		this.description = null;
		this.pageType = 0;
		this.state = null;
	}

	static serialize(value: SavedViewRequest | null): Uint8Array {
		const writer = MemoryPackWriter.getSharedInstance();
		this.serializeCore(writer, value);
		return writer.toArray();
	}

	static serializeCore(writer: MemoryPackWriter, value: SavedViewRequest | null): void {
		if (value == null) {
			writer.writeNullObjectHeader();
			return;
		}

		writer.writeObjectHeader(4);
		writer.writeString(value.name);
		writer.writeString(value.description);
		writer.writeInt32(value.pageType);
		writer.writeString(JSON.stringify(value.state ?? null));
	}

	static deserialize(buffer: ArrayBuffer): SavedViewRequest | null {
		return this.deserializeCore(new MemoryPackReader(buffer));
	}

	static deserializeCore(reader: MemoryPackReader): SavedViewRequest | null {
		const [ok, count] = reader.tryReadObjectHeader();
		if (!ok) {
			return null;
		}

		const value = new SavedViewRequest();
		if (count == 4) {
			value.name = reader.readString();
			value.description = reader.readString();
			value.pageType = reader.readInt32();
			value.state = JSON.parse(reader.readString() ?? 'null');
		} else if (count > 4) {
			throw new Error("Current object's property count is larger than type schema, can't deserialize about versioning.");
		} else {
			if (count == 0) return value;
			value.name = reader.readString();
			if (count == 1) return value;
			value.description = reader.readString();
			if (count == 2) return value;
			value.pageType = reader.readInt32();
			if (count == 3) return value;
			value.state = JSON.parse(reader.readString() ?? 'null');
			if (count == 4) return value;
		}
		return value;
	}
}
